using Newtonsoft.Json;

namespace RosSharp.RosBridgeClient.MessageTypes.Backend
{
    public class SetInt16Request : Message
    {
        [JsonIgnore]
        public const string RosMessageName = "backend/SetInt16";
        public short data;
        public SetInt16Request() { this.data = 0; }
        public SetInt16Request(short data) { this.data = data; }
    }

    public class SetInt16Response : Message
    {
        [JsonIgnore]
        public const string RosMessageName = "backend/SetInt16";
        public bool success;
        public string message;
        public SetInt16Response() { this.success = false; this.message = ""; }
    }

    public class SetFloat32Request : Message
    {
        [JsonIgnore]
        public const string RosMessageName = "backend/SetFloat32";
        public float data;
        public SetFloat32Request() { this.data = 0.0f; }
        public SetFloat32Request(float data) { this.data = data; }
    }

    public class SetFloat32Response : Message
    {
        [JsonIgnore]
        public const string RosMessageName = "backend/SetFloat32";
        public bool success;
        public string message;
        public SetFloat32Response() { this.success = false; this.message = ""; }
    }
}