namespace MKVToolNixWrapper.Dtos
{
    public class FileMeta
    {
        public string FilePath { get; set; }
        public bool Include { get; set; }
        public FileStatusEnum Status { get; set; }
    }
}