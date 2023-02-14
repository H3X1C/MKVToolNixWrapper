namespace MKVToolNixWrapper.Dtos
{
    public class TrackList
    {
        public double Id { get; set; }
        public string Name { get; set; }
        public string Language { get; set; }
        public string Type { get; set; }
        public string Codec { get; set; }
        public bool Include { get; set; }
        public bool Default { get; set; }
        public bool Forced { get; set; }
    }
}