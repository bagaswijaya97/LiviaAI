public class GeminiRequest
{
    public string prompt { get; set; } = string.Empty;
    public string? session_id { get; set; }
    public string? model { get; set; }
}

#region TextOnly Request
public class Root
{
    public List<Content> contents { get; set; } = new List<Content>();
}

public class Content
{
    public List<Part> parts { get; set; } = new List<Part>();
}

public class Part
{
    public string? text { get; set; }
    public InlineData? inlineData { get; set; }
}

public class InlineData
{
    public string? mimeType { get; set; }
    public byte[]? data { get; set; }
}

#endregion
