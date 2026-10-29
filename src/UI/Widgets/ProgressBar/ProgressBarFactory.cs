using UI.Builder;
using UnityEngine;

namespace PrettyConvenientMenu.UI.Widgets.ProgressBar
{
    public static class ProgressBarFactory
    {
        public static void CreateCommercialBar(
            UIPanelBuilder builder,
            float stockRatio,
            float deliveryRatio = 0f,
            float outgoingRatio = 0f,
            float committedRatio = 0f,
            float inboundRatio = 0f)
        {
            CreateStandardTransferBar(
                builder,
                stockRatio,
                deliveryRatio,
                outgoingRatio,
                committedRatio,
                inboundRatio,
                stockColor: new Color(0.94f, 0.92f, 0.88f, 1f),
                deliveryColor: new Color(0.30f, 0.69f, 0.31f, 0.85f),
                outgoingColor: new Color(1.0f, 0.60f, 0.0f, 1f)
            );
        }

        public static void CreateEngineSupplyBar(
            UIPanelBuilder builder,
            float stockRatio,
            float deliveryRatio = 0f,
            float outgoingRatio = 0f,
            float committedRatio = 0f,
            float inboundRatio = 0f)
        {
            CreateStandardTransferBar(
                builder,
                stockRatio,
                deliveryRatio,
                outgoingRatio,
                committedRatio,
                inboundRatio,
                stockColor: new Color(0.94f, 0.92f, 0.88f, 1f),
                deliveryColor: new Color(0.30f, 0.69f, 0.31f, 0.85f),
                outgoingColor: new Color(1.0f, 0.60f, 0.0f, 1f)
            );
        }

        public static void CreateMowBar(
            UIPanelBuilder builder,
            float stockRatio,
            float deliveryRatio = 0f,
            float outgoingRatio = 0f,
            float committedRatio = 0f,
            float inboundRatio = 0f)
        {
            CreateStandardTransferBar(
                builder,
                stockRatio,
                deliveryRatio,
                outgoingRatio,
                committedRatio,
                inboundRatio,
                stockColor: new Color(0.94f, 0.92f, 0.88f, 1f),
                deliveryColor: new Color(0.30f, 0.69f, 0.31f, 0.85f),
                outgoingColor: new Color(1.0f, 0.60f, 0.0f, 1f)
            );
        }

        public static void CreateRepairPartsBar(
            UIPanelBuilder builder,
            float baseRatio,
            float unloadingRatio,
            float programRatio,
            float spotRatio,
            float arrivingRatio)
        {
            SquareProgressBar bar = new SquareProgressBar();

            Color stockColor = new Color(0.94f, 0.92f, 0.88f, 1f);
            Color greenColor = new Color(0.30f, 0.69f, 0.31f, 0.95f);
            Color orangeColor = new Color(1.0f, 0.60f, 0.0f, 1f);
            Color cyanColor = new Color(0.30f, 0.82f, 0.88f, 1f);

            // 1. Base Stock in storage bin (Solid Cream/White forward)
            if (baseRatio > 0.001f)
            {
                bar.AddBar(baseRatio, SegmentStyle.Solid(stockColor), StackDirection.Forward);
            }

            // 2. Unloading cars parked on track (Solid Green forward adding to base stock before demand)
            if (unloadingRatio > 0.001f)
            {
                bar.AddBar(unloadingRatio, SegmentStyle.Solid(greenColor), StackDirection.Forward);
            }

            // 3. Program Demand (Striped Orange reducing backward from on-site stock)
            if (programRatio > 0.001f)
            {
                bar.AddBar(programRatio, SegmentStyle.Striped(orangeColor), StackDirection.Backward);
            }

            // 4. Spot Demand (Striped Cyan reducing backward from program balance)
            if (spotRatio > 0.001f)
            {
                bar.AddBar(spotRatio, SegmentStyle.Striped(cyanColor), StackDirection.Backward);
            }

            // 5. Caliper for arriving en-route shipments (projecting forward)
            if (arrivingRatio > 0.001f)
            {
                float caliperOrigin = Mathf.Clamp01(baseRatio + unloadingRatio);
                bar.WithCaliper(origin: caliperOrigin, rightSpan: arrivingRatio, rightColor: greenColor);
            }

            bar.Build(builder);
        }

        private static void CreateStandardTransferBar(
            UIPanelBuilder builder,
            float stockRatio,
            float deliveryRatio,
            float outgoingRatio,
            float committedRatio,
            float inboundRatio,
            Color stockColor,
            Color deliveryColor,
            Color outgoingColor)
        {
            SquareProgressBar bar = new SquareProgressBar();

            // 1. Normal Stock (extends forward)
            if (stockRatio > 0.001f)
            {
                bar.AddBar(stockRatio, SegmentStyle.Solid(stockColor), StackDirection.Forward);
            }

            // 2. Current Deliveries (extends forward onto stock)
            if (deliveryRatio > 0.001f)
            {
                bar.AddBar(deliveryRatio, SegmentStyle.Solid(deliveryColor), StackDirection.Forward);
            }

            // 3. Current Outgoing (carves backward from top, striped)
            if (outgoingRatio > 0.001f)
            {
                bar.AddBar(outgoingRatio, SegmentStyle.Striped(outgoingColor), StackDirection.Backward);
            }

            // 4. Transition Needle
            float totalAdded = stockRatio + deliveryRatio;
            float netActivePoint = Mathf.Clamp01(outgoingRatio > 0.001f ? totalAdded - outgoingRatio : totalAdded);
            if (deliveryRatio > 0.001f || outgoingRatio > 0.001f)
            {
                bar.WithMarker(netActivePoint, Color.white);
            }

            // 5. Caliper Projections
            if (committedRatio > 0.001f || inboundRatio > 0.001f)
            {
                float? left = committedRatio > 0.001f ? committedRatio : null;
                float? right = inboundRatio > 0.001f ? inboundRatio : null;
                bar.WithCaliper(
                    origin: netActivePoint,
                    leftSpan: left,
                    leftColor: new Color(1.0f, 0.60f, 0.0f, 0.95f),
                    rightSpan: right,
                    rightColor: new Color(0.30f, 0.69f, 0.31f, 0.95f)
                );
            }

            bar.Build(builder);
        }
    }
}
