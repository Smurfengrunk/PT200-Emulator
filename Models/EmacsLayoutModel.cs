namespace PT200Emulator.Models
{
    public class EmacsLayoutModel
    {
        public int Rows { get; set; }
        public int Cols { get; set; }
        public List<EmacsField> Fields { get; set; } = new();
        public bool IsActive => Fields.Count > 0;

        public static EmacsLayoutModel Parse(byte[] data)
        {
            var model = new EmacsLayoutModel();

            // Exempel: första bytes = typ, cols, rows
            model.Cols = data[2];
            model.Rows = data[3];

            // Resten: fältdefinitioner (mockad tolkning)
            for (int i = 4; i < data.Length; i += 4)
            {
                var field = new EmacsField
                {
                    Row = data[i],
                    Col = data[i + 1],
                    Length = data[i + 2],
                    Reverse = (data[i + 3] & 0x01) != 0,
                    Type = "input"
                };
                model.Fields.Add(field);
            }

            return model;
        }
    }

    public class EmacsField
    {
        public int Row { get; set; }
        public int Col { get; set; }
        public int Length { get; set; }
        public string Type { get; set; } = "input";
        public bool Reverse { get; set; }
    }
}