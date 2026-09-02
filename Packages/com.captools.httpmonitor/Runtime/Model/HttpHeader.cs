namespace HttpMonitor
{
    /// <summary>One header line. Order and duplicates are preserved as seen on the wire.</summary>
    public readonly struct HttpHeader
    {
        public string Name { get; }
        public string Value { get; }

        public HttpHeader(string name, string value)
        {
            Name = name ?? string.Empty;
            Value = value ?? string.Empty;
        }

        public override string ToString()
        {
            return Name + ": " + Value;
        }
    }
}
