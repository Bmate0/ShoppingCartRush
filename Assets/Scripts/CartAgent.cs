using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class CartAgent : Agent
{
    [Header("Hivatkozások")]
    public Transform[] items;            // a bevásárlólista termékei (Tag: Target)
    public Transform checkout;           // a pénztár (Tag: Checkout)
    public ShopperNPC[] shoppers;

    [Header("Mozgás")]
    public float pushAcceleration = 8f;
    public float strafeAcceleration = 4f;
    public float turnSpeed = 150f;

    [Header("Rakomány")]
    public float emptyMass = 20f;        // üres kocsi tömege
    public float massPerItem = 10f;      // ennyivel nő termékenként

    [Header("Jutalmak")]
    public float itemReward = 0.5f;
    public float checkoutReward = 1f;
    public float progressRewardPerMeter = 0.05f;
    public float collisionPenalty = -0.05f;
    public float scrapePenalty = -0.001f;
    public float shopperHitPenalty = -0.5f;

    readonly float[] shelfX = { -9f, -3f, 3f, 9f };
    readonly float[] aisleX = { -12.25f, -6f, 0f, 6f, 12.25f };
    const float targetSideOffset = 1.35f;

    Rigidbody rb;
    bool[] collected;
    int activeItems;
    int collectedCount;
    float prevDistance;

    // statisztikák
    bool finished;
    bool hitShopper;
    int episodeCount;

    public override void Initialize()
    {
        rb = GetComponent<Rigidbody>();
        collected = new bool[items.Length];
    }

    public override void OnEpisodeBegin()
    {
        // --- Az előző epizód eredményei a TensorBoardba ---
        if (episodeCount > 0)
        {
            var stats = Academy.Instance.StatsRecorder;
            stats.Add("Shop/SuccessRate", finished ? 1f : 0f);
            stats.Add("Shop/ShopperHitRate", hitShopper ? 1f : 0f);
            stats.Add("Shop/ItemsCollected", collectedCount);
        }
        episodeCount++;
        finished = false;
        hitShopper = false;

        // --- Kocsi ---
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        float ax = aisleX[Random.Range(0, aisleX.Length)] + Random.Range(-1f, 1f);
        transform.localPosition = new Vector3(ax, 0f, Random.Range(-12f, 12f));
        transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        // --- Bevásárlólista (hossza a curriculumból állítható, alapból az összes) ---
        activeItems = Mathf.Clamp(Mathf.RoundToInt(
            Academy.Instance.EnvironmentParameters.GetWithDefault("list_size", items.Length)),
            1, items.Length);

        collectedCount = 0;
        for (int i = 0; i < items.Length; i++)
        {
            bool onList = i < activeItems;
            collected[i] = !onList;                     // ami nincs a listán, az "kész"
            items[i].gameObject.SetActive(onList);
            if (onList) PlaceItem(i);
        }

        checkout.gameObject.SetActive(false);           // csak a lista végén jelenik meg
        UpdateMass();

        // --- Vásárlók ---
        int activeShoppers = Mathf.RoundToInt(
            Academy.Instance.EnvironmentParameters.GetWithDefault("shopper_count", shoppers.Length));
        for (int i = 0; i < shoppers.Length; i++)
        {
            bool on = i < activeShoppers;
            shoppers[i].gameObject.SetActive(on);
            if (on) shoppers[i].ResetShopper(transform);
        }

        prevDistance = CurrentGoalDistance();
    }

    void PlaceItem(int i)
    {
        Vector3 p;
        int tries = 0;
        do
        {
            float sx = shelfX[Random.Range(0, shelfX.Length)];
            float side = Random.value < 0.5f ? -1f : 1f;
            p = new Vector3(sx + side * targetSideOffset, 0.5f, Random.Range(-6f, 6f));
            tries++;
        } while (tries < 30 && !IsFreeSpot(p, i));
        items[i].localPosition = p;
    }

    bool IsFreeSpot(Vector3 p, int i)
    {
        if (FlatDistance(p, transform.localPosition) < 4f) return false;   // ne a kocsi mellett
        for (int j = 0; j < i; j++)                                          // ne egy másik termék mellett
            if (!collected[j] && FlatDistance(p, items[j].localPosition) < 3f) return false;
        return true;
    }

    void UpdateMass()
    {
        rb.mass = emptyMass + massPerItem * collectedCount;
    }

    // Az aktuális cél: a legközelebbi még meg nem vett termék, utána a pénztár
    Vector3 CurrentGoalPosition()
    {
        if (collectedCount >= activeItems) return checkout.position;

        float best = float.MaxValue;
        Vector3 bestPos = checkout.position;
        for (int i = 0; i < items.Length; i++)
        {
            if (collected[i]) continue;
            float d = FlatDistance(items[i].position, transform.position);
            if (d < best) { best = d; bestPos = items[i].position; }
        }
        return bestPos;
    }

    float CurrentGoalDistance()
    {
        return FlatDistance(CurrentGoalPosition(), transform.position);
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // Saját sebesség (2)
        Vector3 localVel = transform.InverseTransformDirection(rb.linearVelocity);
        sensor.AddObservation(localVel.x / 5f);
        sensor.AddObservation(localVel.z / 5f);

        // Termékek: megvan-e (1) + irány (2) + távolság (1) → 3 × 4 = 12
        for (int i = 0; i < items.Length; i++)
        {
            if (collected[i])
            {
                sensor.AddObservation(1f);
                sensor.AddObservation(0f);
                sensor.AddObservation(0f);
                sensor.AddObservation(0f);
            }
            else
            {
                sensor.AddObservation(0f);
                AddDirection(sensor, items[i].position);
            }
        }

        // Pénztár: nyitva van-e (1) + irány (2) + távolság (1) → 4
        sensor.AddObservation(collectedCount >= activeItems ? 1f : 0f);
        AddDirection(sensor, checkout.position);

        // Rakomány telítettsége (1)
        sensor.AddObservation((float)collectedCount / items.Length);
        // Összesen: 2 + 12 + 4 + 1 = 19
    }

    void AddDirection(VectorSensor sensor, Vector3 worldPos)
    {
        Vector3 to = worldPos - transform.position;
        to.y = 0f;
        Vector3 localDir = transform.InverseTransformDirection(to.normalized);
        sensor.AddObservation(localDir.x);
        sensor.AddObservation(localDir.z);
        sensor.AddObservation(to.magnitude / 40f);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        float push = Mathf.Clamp(actions.ContinuousActions[0], -1f, 1f);
        float steer = Mathf.Clamp(actions.ContinuousActions[1], -1f, 1f);
        float strafe = Mathf.Clamp(actions.ContinuousActions[2], -1f, 1f);

        // Ugyanakkora tolóerő, mint üresen: nehezebb kocsi → lassabb gyorsulás és fékezés
        Vector3 force = (transform.forward * push * pushAcceleration
                       + transform.right * strafe * strafeAcceleration) * emptyMass;
        rb.AddForce(force, ForceMode.Force);

        // Nehezebb kocsi lassabban fordul
        float turnFactor = Mathf.Sqrt(emptyMass / rb.mass);
        Quaternion turn = Quaternion.Euler(0f, steer * turnSpeed * turnFactor * Time.fixedDeltaTime, 0f);
        rb.MoveRotation(rb.rotation * turn);

        // Közeledési jutalom az aktuális célhoz
        float d = CurrentGoalDistance();
        AddReward((prevDistance - d) * progressRewardPerMeter);
        prevDistance = d;

        if (MaxStep > 0)
            AddReward(-1f / MaxStep);
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var a = actionsOut.ContinuousActions;
        a[0] = 0f; a[1] = 0f; a[2] = 0f;

        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb.wKey.isPressed) a[0] += 1f;
        if (kb.sKey.isPressed) a[0] -= 1f;
        if (kb.dKey.isPressed) a[1] += 1f;
        if (kb.aKey.isPressed) a[1] -= 1f;
        if (kb.eKey.isPressed) a[2] += 1f;
        if (kb.qKey.isPressed) a[2] -= 1f;
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Target"))
        {
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i] == other.transform && !collected[i])
                {
                    collected[i] = true;
                    collectedCount++;
                    items[i].gameObject.SetActive(false);
                    AddReward(itemReward);
                    UpdateMass();

                    if (collectedCount >= activeItems)
                        checkout.gameObject.SetActive(true);   // irány a pénztár

                    prevDistance = CurrentGoalDistance();      // új cél → új kiindulási távolság
                    break;
                }
            }
        }
        else if (other.CompareTag("Checkout"))
        {
            AddReward(checkoutReward);
            finished = true;
            EndEpisode();
        }
    }

    void OnCollisionEnter(Collision c)
    {
        if (c.collider.CompareTag("Shopper"))
        {
            AddReward(shopperHitPenalty);
            hitShopper = true;
            EndEpisode();
            return;
        }

        if (c.collider.CompareTag("Wall") || c.collider.CompareTag("Shelf"))
            AddReward(collisionPenalty);
    }

    void OnCollisionStay(Collision c)
    {
        if (c.collider.CompareTag("Wall") || c.collider.CompareTag("Shelf"))
            AddReward(scrapePenalty);
    }

    static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f; b.y = 0f;
        return Vector3.Distance(a, b);
    }
}