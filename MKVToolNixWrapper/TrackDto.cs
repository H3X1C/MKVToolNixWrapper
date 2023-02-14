using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MKVToolNixWrapper
{
    public class Properties
    {
        public double uid { get; set; }
    }

    public class Attachment
    {
        public string content_type { get; set; }
        public string description { get; set; }
        public string file_name { get; set; }
        public double id { get; set; }
        public Properties properties { get; set; }
        public double size { get; set; }
    }

    public class Chapter
    {
        public double num_entries { get; set; }
    }

    public class ContainerProperties
    {
        public double container_type { get; set; }
        public string date_local { get; set; }
        public string date_utc { get; set; }
        public long duration { get; set; }
        public bool is_providing_timestamps { get; set; }
        public string muxing_application { get; set; }
        public string segment_uid { get; set; }
        public string title { get; set; }
        public string writing_application { get; set; }
    }

    public class Container
    {
        public ContainerProperties properties { get; set; }
        public bool recognized { get; set; }
        public bool supported { get; set; }
        public string type { get; set; }
    }

    public class Properties2
    {
        public string codec_id { get; set; }
        public string codec_private_data { get; set; }
        public double codec_private_length { get; set; }
        public double default_duration { get; set; }
        public bool default_track { get; set; }
        public bool enabled_track { get; set; }
        public bool forced_track { get; set; }
        public string language { get; set; }
        public double minimum_timestamp { get; set; }
        public double number { get; set; }
        public string packetizer { get; set; }
        public string pixel_dimensions { get; set; }
        public double uid { get; set; }
        public string track_name { get; set; }
    }

    public class Track
    {
        public string codec { get; set; }
        public double id { get; set; }
        public Properties2 properties { get; set; }
        public string type { get; set; }
    }

    public class RootObject
    {
        public List<Attachment> attachments { get; set; }
        public List<Chapter> chapters { get; set; }
        public Container container { get; set; }
        public List<object> errors { get; set; }
        public string file_name { get; set; }
        public List<object> global_tags { get; set; }
        public double identification_format_version { get; set; }
        public List<object> track_tags { get; set; }
        public List<Track> tracks { get; set; }
    }
}
