using System;
using System.Collections;
using System.Reflection;

namespace PrettyConvenientMenu.Integration
{
    public readonly struct MowReserveInfo
    {
        public bool IsActive { get; }
        public float Stock { get; }
        public float Cap { get; }
        public float ReserveDays { get; }

        public MowReserveInfo(bool isActive, float stock, float cap, float reserveDays)
        {
            IsActive = isActive;
            Stock = stock;
            Cap = cap;
            ReserveDays = reserveDays;
        }

        public bool IsRefilling => IsActive && Cap > 0.001f && Stock < (Cap - 0.001f);
    }

    public static class MowProducerPriorityAdapter
    {
        private const string TargetFeedName = "mow-producer-priority";
        private const int ExpectedFeedVersion = 1;

        private static bool _initialized;
        private static Func<object>? _feedReadDelegate;

        public static bool IsModInstalled
        {
            get
            {
                EnsureInitialized();
                return _feedReadDelegate != null;
            }
        }

        public static void Reset()
        {
            _initialized = false;
            _feedReadDelegate = null;
        }

        private static void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.IsDynamic || asm.FullName.StartsWith("System") || asm.FullName.StartsWith("mscorlib") || asm.FullName.StartsWith("Unity"))
                    {
                        continue;
                    }

                    Type? apiType = asm.GetType("MowProducerPriority.Api");
                    if (apiType == null)
                    {
                        continue;
                    }

                    FieldInfo? feedField = apiType.GetField("Feed", BindingFlags.Public | BindingFlags.Static);
                    if (feedField != null && feedField.IsLiteral && TargetFeedName.Equals(feedField.GetRawConstantValue() as string, StringComparison.OrdinalIgnoreCase))
                    {
                        MethodInfo? readMethod = apiType.GetMethod("Read", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                        if (readMethod != null && typeof(object).IsAssignableFrom(readMethod.ReturnType))
                        {
                            _feedReadDelegate = (Func<object>)Delegate.CreateDelegate(typeof(Func<object>), readMethod);
                            break;
                        }
                    }
                }
            }
            catch
            {
                _feedReadDelegate = null;
            }
        }

        public static bool TryGetReserveInfo(string? industryId, string? loadId, string? sellerId, out MowReserveInfo info)
        {
            info = default;
            EnsureInitialized();

            if (_feedReadDelegate == null || string.IsNullOrEmpty(industryId) || string.IsNullOrEmpty(loadId))
            {
                return false;
            }

            try
            {
                object? result = _feedReadDelegate();
                if (result is not IDictionary feedDict || feedDict.Contains("error"))
                {
                    return false;
                }

                int apiVersion = feedDict.Contains("api") && feedDict["api"] != null ? Convert.ToInt32(feedDict["api"]) : 0;
                if (apiVersion != ExpectedFeedVersion)
                {
                    return false;
                }

                bool feedActive = feedDict.Contains("active") && feedDict["active"] != null && Convert.ToBoolean(feedDict["active"]);
                float reserveDays = feedDict.Contains("reserveDays") && feedDict["reserveDays"] != null ? Convert.ToSingle(feedDict["reserveDays"]) : 1f;

                if (!feedDict.Contains("piles") || feedDict["piles"] is not IEnumerable pilesList)
                {
                    return false;
                }

                foreach (object pileObj in pilesList)
                {
                    if (pileObj is not IDictionary pileDict) continue;

                    string? pIndustryId = pileDict["industryId"] as string;
                    string? pLoadId = pileDict["loadId"] as string;
                    string? pSellerId = pileDict["sellerId"] as string;

                    if (!string.Equals(pIndustryId, industryId, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(pLoadId, loadId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(sellerId) && !string.IsNullOrEmpty(pSellerId) &&
                        !string.Equals(pSellerId, sellerId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    bool pileActive = pileDict.Contains("active") && pileDict["active"] != null && Convert.ToBoolean(pileDict["active"]);
                    float stock = pileDict.Contains("stock") && pileDict["stock"] != null ? Convert.ToSingle(pileDict["stock"]) : 0f;
                    float cap = pileDict.Contains("cap") && pileDict["cap"] != null ? Convert.ToSingle(pileDict["cap"]) : 0f;

                    info = new MowReserveInfo(pileActive, stock, cap, reserveDays);
                    return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }
    }
}
