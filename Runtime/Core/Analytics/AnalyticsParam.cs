namespace NineDTech.AdsKit.Analytics
{
    public enum AnalyticsParamType
    {
        String,
        Long,
        Double
    }

    /// <summary>One event parameter. Firebase supports string, long and double values.</summary>
    public readonly struct AnalyticsParam
    {
        public readonly string Name;
        public readonly AnalyticsParamType Type;
        public readonly string StringValue;
        public readonly long LongValue;
        public readonly double DoubleValue;

        private AnalyticsParam(string name, AnalyticsParamType type, string s, long l, double d)
        {
            Name = name;
            Type = type;
            StringValue = s;
            LongValue = l;
            DoubleValue = d;
        }

        public static AnalyticsParam Of(string name, string value) =>
            new AnalyticsParam(name, AnalyticsParamType.String, value ?? "", 0, 0);

        public static AnalyticsParam Of(string name, long value) =>
            new AnalyticsParam(name, AnalyticsParamType.Long, null, value, 0);

        public static AnalyticsParam Of(string name, double value) =>
            new AnalyticsParam(name, AnalyticsParamType.Double, null, 0, value);

        public override string ToString()
        {
            switch (Type)
            {
                case AnalyticsParamType.String: return Name + "=\"" + StringValue + "\"";
                case AnalyticsParamType.Long: return Name + "=" + LongValue;
                default: return Name + "=" + DoubleValue.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
            }
        }
    }
}
