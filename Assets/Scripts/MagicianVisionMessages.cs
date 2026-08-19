using Newtonsoft.Json;
using RosSharp.RosBridgeClient.MessageTypes.Std;

namespace RosSharp.RosBridgeClient.MessageTypes.MagicianVision
{
    /// <summary>
    /// MAGICIAN magician_vision_classifier/msg/Detection.msg mesajının ROS# karşılığı.
    /// Kaynak: liveClassifierTorchROS.py "detections" topic'i.
    /// x,y = tespit tile'ının SOL-ÜST köşesi (piksel), w,h = tile boyutu (piksel).
    /// Kusur merkezi = (x + w/2, y + h/2). depth = lazerlerden IDW ile interpole
    /// edilen mesafe (metre; lazerler kapalıysa 0, geçersizse NaN olabilir).
    /// </summary>
    public class Detection : Message
    {
        [JsonIgnore]
        public const string RosMessageName = "magician_vision_classifier/Detection";

        public Header header;
        public int x;
        public int y;
        public int w;
        public int h;
        public float depth;
        public string type;
        public string class_name;
        public float probability;

        public Detection()
        {
            header = new Header();
            x = 0;
            y = 0;
            w = 0;
            h = 0;
            depth = 0.0f;
            type = "";
            class_name = "";
            probability = 0.0f;
        }
    }
}