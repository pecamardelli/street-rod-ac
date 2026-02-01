using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Models.Career.Filters
{
    /// <summary>
    /// A composite filter that combines multiple filters with AND/OR logic.
    /// Example: "American muscle" = OriginFilter("USA") AND PowerFilter(300+)
    /// </summary>
    public class CompositeCarFilter : ICarFilter
    {
        public string FilterType => "Composite";

        /// <summary>
        /// Child filters to evaluate
        /// </summary>
        public List<ICarFilter> Filters { get; set; } = [];

        /// <summary>
        /// If true, ALL filters must match (AND logic).
        /// If false, ANY filter matching is sufficient (OR logic).
        /// </summary>
        public bool RequireAll { get; set; } = true;

        public string DisplayDescription
        {
            get
            {
                if (Filters.Count == 0)
                    return "No requirements";

                var descriptions = Filters.Select(f => f.DisplayDescription);
                var separator = RequireAll ? " + " : " or ";
                return string.Join(separator, descriptions);
            }
        }

        public CompositeCarFilter() { }

        public CompositeCarFilter(bool requireAll, params ICarFilter[] filters)
        {
            RequireAll = requireAll;
            Filters = [.. filters];
        }

        /// <summary>
        /// Creates an AND composite (all filters must match)
        /// </summary>
        public static CompositeCarFilter And(params ICarFilter[] filters)
        {
            return new CompositeCarFilter(true, filters);
        }

        /// <summary>
        /// Creates an OR composite (any filter can match)
        /// </summary>
        public static CompositeCarFilter Or(params ICarFilter[] filters)
        {
            return new CompositeCarFilter(false, filters);
        }

        public bool Matches(CarDefinition car, Car? instance = null)
        {
            if (Filters.Count == 0)
                return true;

            if (RequireAll)
            {
                // AND logic: all filters must match
                return Filters.All(f => f.Matches(car, instance));
            }
            else
            {
                // OR logic: any filter can match
                return Filters.Any(f => f.Matches(car, instance));
            }
        }
    }
}
