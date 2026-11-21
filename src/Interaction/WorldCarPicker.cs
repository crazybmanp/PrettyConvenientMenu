using System;
using System.Collections.Generic;
using System.Linq;
using Helpers;
using Model;
using RollingStock;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PrettyConvenientMenu.Interaction
{
    /// <summary>
    /// Interactive 3D world multi-car selector.
    /// Allows clicking player-owned rolling stock in the world (Shift+Click for coupled cut)
    /// to build a pending selection, then confirming or cancelling back to the caller.
    /// </summary>
    [DefaultExecutionOrder(-120)]
    public sealed class WorldCarPicker : MonoBehaviour
    {
        private const float MaxRaycastDistance = 3000f;

        public static WorldCarPicker? Shared { get; private set; }

        public event Action? OnSelectionChanged;

        private readonly List<Car> pendingCars = new List<Car>();
        private readonly HashSet<string> pendingCarIds = new HashSet<string>(StringComparer.Ordinal);
        private Action<IReadOnlyList<Car>>? onConfirmCallback;
        private Action? onCancelCallback;
        private Camera? cachedCamera;

        public bool IsActive { get; private set; }
        public IReadOnlyList<Car> PendingCars => pendingCars;

        private void Awake()
        {
            Shared = this;
        }

        private void OnDestroy()
        {
            Cancel();
            if (Shared == this)
            {
                Shared = null;
            }
        }

        public void BeginPicking(Action<IReadOnlyList<Car>> onConfirm, Action? onCancel = null)
        {
            pendingCars.Clear();
            pendingCarIds.Clear();
            onConfirmCallback = onConfirm;
            onCancelCallback = onCancel;
            IsActive = true;
            OnSelectionChanged?.Invoke();
        }

        public void Confirm()
        {
            if (!IsActive)
            {
                return;
            }

            List<Car> confirmed = new List<Car>(pendingCars);
            Action<IReadOnlyList<Car>>? callback = onConfirmCallback;

            ResetState();
            callback?.Invoke(confirmed);
        }

        public void Cancel()
        {
            if (!IsActive)
            {
                return;
            }

            Action? callback = onCancelCallback;
            ResetState();
            callback?.Invoke();
        }

        public void RemovePendingCar(Car car)
        {
            if (car == null || string.IsNullOrEmpty(car.id))
            {
                return;
            }

            if (pendingCarIds.Remove(car.id))
            {
                pendingCars.RemoveAll(c => c == null || c.id == car.id);
                OnSelectionChanged?.Invoke();
            }
        }

        private void ResetState()
        {
            IsActive = false;
            pendingCars.Clear();
            pendingCarIds.Clear();
            onConfirmCallback = null;
            onCancelCallback = null;
            OnSelectionChanged?.Invoke();
        }

        private void Update()
        {
            if (!IsActive)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Cancel();
                return;
            }

            if (!Input.GetMouseButtonDown(0))
            {
                return;
            }

            // Ignore clicks over UI elements
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                return;
            }

            if (!MainCameraHelper.TryGetIfNeeded(ref cachedCamera) || cachedCamera == null)
            {
                return;
            }

            Ray ray = cachedCamera.ScreenPointToRay(Input.mousePosition);
            int mask = (1 << Layers.Clickable) | (1 << Layers.Default);
            if (!Physics.Raycast(ray, out RaycastHit hit, MaxRaycastDistance, mask))
            {
                return;
            }

            CarPickable? pickable = hit.collider != null ? hit.collider.GetComponentInParent<CarPickable>() : null;
            Car? hitCar = pickable?.car;
            if (!Data.SupplyFleet.SupplyFleetStore.IsEligibleSupplyFleetCar(hitCar))
            {
                return;
            }

            bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (shiftHeld)
            {
                ToggleCoupledCut(hitCar!);
            }
            else
            {
                ToggleSingleCar(hitCar!);
            }
        }

        private void ToggleSingleCar(Car car)
        {
            if (pendingCarIds.Contains(car.id))
            {
                pendingCarIds.Remove(car.id);
                pendingCars.RemoveAll(c => c == null || c.id == car.id);
            }
            else
            {
                pendingCarIds.Add(car.id);
                pendingCars.Add(car);
            }

            OnSelectionChanged?.Invoke();
        }

        private void ToggleCoupledCut(Car anchorCar)
        {
            List<Car> cut = anchorCar.EnumerateCoupled()
                .Where(Data.SupplyFleet.SupplyFleetStore.IsEligibleSupplyFleetCar)
                .ToList();

            if (cut.Count == 0)
            {
                ToggleSingleCar(anchorCar);
                return;
            }

            bool allSelected = cut.All(c => pendingCarIds.Contains(c.id));
            if (allSelected)
            {
                foreach (Car c in cut)
                {
                    pendingCarIds.Remove(c.id);
                }
                pendingCars.RemoveAll(c => c == null || !pendingCarIds.Contains(c.id));
            }
            else
            {
                foreach (Car c in cut)
                {
                    if (pendingCarIds.Add(c.id))
                    {
                        pendingCars.Add(c);
                    }
                }
            }

            OnSelectionChanged?.Invoke();
        }
    }
}
