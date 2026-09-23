using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Services.Dealers
{
    /// <summary>
    /// Lays cars out on a lot to fit the room they are standing in.
    ///
    /// Worked out rather than authored: a lot has to hold anything from four cars to twenty, in rooms that
    /// run from a 28 m shed to an 80 m yard, and hand-written bays cannot cover that. Rows face each other
    /// across an aisle, angled, the way a real lot is set out.
    /// </summary>
    public static class LotLayout
    {
        /// <summary>
        /// Along a row, middle to middle. A 1970s American saloon is a little over 2 m wide, so this leaves
        /// room to open a door without standing in the next car.
        /// </summary>
        private const float CarPitch = 4.6f;

        /// <summary>
        /// Between rows. The longest thing likely to be on a lot is near 5.8 m, so this is its length plus
        /// enough aisle to walk down and to drive one out.
        /// </summary>
        private const float RowPitch = 10.0f;

        /// <summary>Kept clear of the walls so no car is standing in one</summary>
        private const float WallMargin = 2.0f;

        /// <summary>Rows face each other across the aisle, a little off square</summary>
        private const float NearRowHeading = 200f;
        private const float FarRowHeading = 20f;

        /// <summary>
        /// Where to stand <paramref name="cars"/> cars in <paramref name="showroom"/>. Never returns more
        /// bays than the room will hold.
        /// </summary>
        public static List<LotBay> Build(int cars, ShowroomSpec showroom)
        {
            var bays = new List<LotBay>();
            if (cars <= 0) return bays;

            cars = Math.Min(cars, Math.Max(1, showroom.Capacity));

            // Keep the block roughly square so the camera can take it in from one place
            var usableWidth = Math.Max(CarPitch, Math.Min(showroom.FloorWidth, showroom.WallRadius * 2f) - WallMargin * 2f);
            var usableDepth = Math.Max(RowPitch, Math.Min(showroom.FloorDepth, showroom.WallRadius * 2f) - WallMargin * 2f);

            var widthAllows = Math.Max(1, (int)(usableWidth / CarPitch));
            var depthAllows = Math.Max(1, (int)(usableDepth / RowPitch));

            var perRow = Math.Min(widthAllows, Math.Max(2, (int)Math.Ceiling(Math.Sqrt(cars))));
            var rows = (int)Math.Ceiling(cars / (double)perRow);

            // Too deep for the room: widen the rows instead, up to what the walls allow
            while (rows > depthAllows && perRow < widthAllows)
            {
                perRow++;
                rows = (int)Math.Ceiling(cars / (double)perRow);
            }

            rows = Math.Min(rows, depthAllows);

            var firstX = -(perRow - 1) * CarPitch / 2f;
            var firstZ = -(rows - 1) * RowPitch / 2f;

            for (var row = 0; row < rows && bays.Count < cars; row++)
            {
                // A short last row is centred rather than left hanging off one end
                var inRow = Math.Min(perRow, cars - bays.Count);
                var rowOffset = (perRow - inRow) * CarPitch / 2f;

                for (var slot = 0; slot < inRow; slot++)
                {
                    bays.Add(new LotBay
                    {
                        X = firstX + rowOffset + slot * CarPitch,
                        Z = firstZ + row * RowPitch,
                        Heading = row % 2 == 0 ? NearRowHeading : FarRowHeading
                    });
                }
            }

            return bays;
        }

        /// <summary>
        /// How far back the camera has to stand to take in a lot of this size, kept inside the walls. A room
        /// too small to see the whole lot from is not a problem: the player swings round it instead.
        /// </summary>
        public static float CameraRadiusFor(IReadOnlyList<LotBay> bays, ShowroomSpec showroom)
        {
            if (bays.Count == 0) return Math.Min(14f, showroom.WallRadius - 1f);

            var halfWidth = bays.Max(b => Math.Abs(b.X)) + CarPitch;
            var halfDepth = bays.Max(b => Math.Abs(b.Z)) + RowPitch / 2f;
            var reach = (float)Math.Sqrt(halfWidth * halfWidth + halfDepth * halfDepth);

            // Enough to hold the lot in frame, with a little air around it
            var wanted = reach * 1.6f + 4f;

            var ceiling = Math.Max(10f, showroom.WallRadius - 1.5f);
            return Math.Clamp(wanted, 10f, ceiling);
        }
    }
}
