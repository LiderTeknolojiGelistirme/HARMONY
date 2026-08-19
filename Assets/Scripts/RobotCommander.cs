using UnityEngine;
using RosSharp.RosBridgeClient;
using srv = RosSharp.RosBridgeClient.MessageTypes.Std;
using bk = RosSharp.RosBridgeClient.MessageTypes.Backend;
using System;

public class RobotCommander : MonoBehaviour
{
    private RosConnector rosConnector;

    void Start()
    {
        rosConnector = GetComponent<RosConnector>();
    }

    public void GoSlider1()  // bir butona bağla
    {
        srv.SetBoolRequest request = new srv.SetBoolRequest(true);
        rosConnector.RosSocket.CallService<srv.SetBoolRequest, srv.SetBoolResponse>(
            "/ros2_comm/slider1/go_pos",
            (response) => Debug.Log("go_pos yanıt: success=" + response.success + " msg=" + response.message),
            request);
    }

public void SetSpeed(int value)
{
    short shortValue = Convert.ToInt16(value);
    var request = new bk.SetInt16Request(shortValue);
    rosConnector.RosSocket.CallService<bk.SetInt16Request, bk.SetInt16Response>(
        "/ros2_comm/speed_set",
        (r) => Debug.Log("speed yanıt: " + r.message),
        request);
}

public void SetSlider1Pos(float pos)
{
    var request = new bk.SetFloat32Request(pos);
    rosConnector.RosSocket.CallService<bk.SetFloat32Request, bk.SetFloat32Response>(
        "/ros2_comm/slider1/set_pos",
        (r) => Debug.Log("slider pos yanıt: " + r.message),
        request);
}
}