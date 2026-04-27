
public class Rootobject
{
    public Class1[] Property1 { get; set; }
}

public class Class1
{
    public object category { get; set; }
    public string resultType { get; set; }
    public string videoId { get; set; }
    public object videoType { get; set; }
    public string title { get; set; }
    public Artist[] artists { get; set; }
    public string views { get; set; }
    public object duration { get; set; }
    public int duration_seconds { get; set; }
    public object thumbnails { get; set; }
    public object year { get; set; }
    public object album { get; set; }
    public bool inLibrary { get; set; }
    public bool pinnedToListenAgain { get; set; }
    public bool isExplicit { get; set; }
    public object artist { get; set; }
    public object shuffleId { get; set; }
    public object radioId { get; set; }
    public object browseId { get; set; }
    public object type { get; set; }
    public object playlistId { get; set; }
    public object itemCount { get; set; }
    public object author { get; set; }
    public bool live { get; set; }
    public object date { get; set; }
    public object podcast { get; set; }
}

public class Artist
{
    public string name { get; set; }
    public string id { get; set; }
}
