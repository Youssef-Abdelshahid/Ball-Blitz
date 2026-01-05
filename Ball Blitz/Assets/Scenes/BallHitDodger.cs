using System.Collections.Generic;
using UnityEngine;

public class BallHitDodger : MonoBehaviour
{
    public bool destroyDodger = false;

    private readonly HashSet<int> eliminatedDodgerIds = new HashSet<int>();

    DodgerWinManager manager;

    void Awake()
    {
        manager = FindObjectOfType<DodgerWinManager>();
        if (manager == null)
            Debug.LogWarning("BallHitDodger: No DodgerWinManager found in scene.");
    }

    void OnCollisionEnter(Collision collision)
    {
        if (!collision.collider.CompareTag("Dodger")) return;

        GameObject dodgerObj = collision.collider.gameObject;

        if (dodgerObj.GetComponent<DodgerControl>() == null && dodgerObj.transform.root != null)
        {
            var root = dodgerObj.transform.root;
            if (root.GetComponent<DodgerControl>() != null)
                dodgerObj = root.gameObject;
        }

        if (dodgerObj.GetComponent<DodgerControl>() == null)
            return;

        int id = dodgerObj.GetInstanceID();

        if (eliminatedDodgerIds.Contains(id))
            return;

        eliminatedDodgerIds.Add(id);

        if (manager != null)
            manager.OnDodgerEliminated();

        if (destroyDodger) Destroy(dodgerObj);
        else dodgerObj.SetActive(false);
    }
}
