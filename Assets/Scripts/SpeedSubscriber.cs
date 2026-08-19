using UnityEngine;
using RosSharp.RosBridgeClient;
using msg = RosSharp.RosBridgeClient.MessageTypes.Std;

public class SpeedSubscriber : MonoBehaviour
{
    private RosConnector rosConnector;
    private string topic = "/ros2_comm/speed";

    void Start()
    {
        rosConnector = GetComponent<RosConnector>();

        rosConnector.RosSocket.Subscribe<msg.Int16>(
            topic,
            ReceiveMessage);
    }

    private void ReceiveMessage(msg.Int16 message)
    {
        // Dikkat: bu callback ana thread'de DEĞİL — UI'ı doğrudan güncelleme
        Debug.Log("Güncel hız: " + message.data);
    }
}