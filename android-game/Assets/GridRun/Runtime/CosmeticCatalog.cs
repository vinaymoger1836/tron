using System;

namespace GridRun
{
    // Stable preview catalog. IDs can later be mapped to approved store products,
    // but this data grants no ownership and contains no price or purchase route.
    public static class CosmeticCatalog
    {
        public const int Version = 1;
        public enum Category { Cycle, Trail, Bundle }

        public sealed class Entry
        {
            public readonly string Id, Name, Description, BodyTheme, TrailTheme;
            public readonly Category Kind;
            public readonly bool Included;

            public Entry(string id, string name, string description, Category kind,
                string bodyTheme, string trailTheme, bool included)
            {
                Id = id; Name = name; Description = description; Kind = kind;
                BodyTheme = bodyTheme; TrailTheme = trailTheme; Included = included;
            }
        }

        public static readonly Entry[] Items =
        {
            new Entry("cycle.grid.v1", "Grid Cycle", "The standard cyan-accent cycle.", Category.Cycle,
                "grid", "grid", true),
            new Entry("cycle.solar.v1", "Solar Cycle", "Warm amber panels on the standard cycle frame.", Category.Cycle,
                "solar", "grid", false),
            new Entry("trail.grid.v1", "Grid Trail", "The standard cyan light wall.", Category.Trail,
                "grid", "grid", true),
            new Entry("trail.solar.v1", "Solar Trail", "An amber light wall with the standard cycle.", Category.Trail,
                "grid", "solar", false),
            new Entry("bundle.grid.v1", "Grid Set", "The standard cycle and trail together.", Category.Bundle,
                "grid", "grid", true),
            new Entry("bundle.solar.v1", "Solar Set", "Solar Cycle and Solar Trail together.", Category.Bundle,
                "solar", "solar", false)
        };

        public static int Count(Category category)
        {
            int count = 0;
            foreach (var item in Items) if (item.Kind == category) count++;
            return count;
        }

        public static Entry At(Category category, int index)
        {
            if (index < 0 || index >= Count(category)) throw new ArgumentOutOfRangeException(nameof(index));
            foreach (var item in Items)
                if (item.Kind == category && index-- == 0) return item;
            throw new InvalidOperationException("Catalog category is incomplete.");
        }
    }
}
