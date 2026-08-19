using UnityEngine;

public class LabelLineAnchor : MonoBehaviour
{
    [SerializeField] private Transform anchor;

    public Transform Anchor => anchor != null ? anchor : transform;
}

