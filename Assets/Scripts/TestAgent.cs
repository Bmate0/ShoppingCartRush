using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

public class TestAgent : Agent
{
    public override void OnEpisodeBegin()
    {
        transform.localPosition = Vector3.zero;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        sensor.AddObservation(transform.localPosition); // 3 szám: x, y, z
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        float move = actions.ContinuousActions[0];
        transform.localPosition += new Vector3(move * 5f * Time.deltaTime, 0f, 0f);

        AddReward(-0.001f);
        if (Mathf.Abs(transform.localPosition.x) > 5f)
            EndEpisode();
    }
}