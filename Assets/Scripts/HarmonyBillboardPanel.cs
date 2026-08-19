using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Paneli sürekli kullanıcıya döndürür.
///
/// Dünya uzayındaki bir Canvas, +Z ekseni izleyiciden panele doğru bakacak
/// şekilde yönlendirildiğinde okunur hale gelir; bu bileşen her karede o
/// yönelimi kurar. Panel taşınırken ya da kullanıcı çevresinde dolaşırken
/// yüzeyin okunabilir kalmasını sağlar.
///
/// Yalnızca dönüşü sürer; konumu <see cref="XRGrabInteractable"/> yönetir.
/// Bu yüzden kavrama bileşeninde "Track Rotation" kapalı olmalıdır, aksi
/// halde ikisi aynı karede birbirinin üzerine yazar.
///
/// XRI etkileşim yöneticisi Update aşamasında çalıştığı için dönüş LateUpdate
/// içinde ve yüksek çalışma sırasıyla uygulanır.
/// </summary>
[DefaultExecutionOrder(100)]
public class HarmonyBillboardPanel : MonoBehaviour
{
    [Header("Hedef")]
    [Tooltip("Panelin bakacağı göz/kamera. Boş bırakılırsa ana kamera kullanılır.")]
    [SerializeField] private Transform viewer;

    [Header("Davranış")]
    [Tooltip("Yalnızca Y ekseninde dön. Kapatılırsa panel kullanıcıya doğru " +
             "eğilir de; duvar panellerinde genelde dik durması tercih edilir.")]
    [SerializeField] private bool yawOnly = true;

    [Tooltip("Dönüşün hedefe yaklaşma hızı [1/s]. 0 = anında, sıçramalı.")]
    [SerializeField] private float rotationSmoothing = 8f;

    [Tooltip("Panel kavranmışken de dönmeye devam etsin mi? Kapatılırsa panel " +
             "taşınırken yönelimi sabit kalır, bırakıldığında kullanıcıya döner.")]
    [SerializeField] private bool billboardWhileGrabbed = true;

    [Tooltip("Kullanıcı bu mesafeden yakına girdiğinde dönüş dondurulur [m]. " +
             "Panelin tam içindeyken yön hesabı anlamsızlaşır ve panel savrulur.")]
    [SerializeField] private float minViewerDistance = 0.2f;

    private XRGrabInteractable grab;

    private void Awake()
    {
        grab = GetComponent<XRGrabInteractable>();
        if (viewer == null && Camera.main != null) viewer = Camera.main.transform;
    }

    private void LateUpdate()
    {
        if (viewer == null)
        {
            // Kamera sahneye sonradan gelmiş olabilir (XR rig kurulumu).
            if (Camera.main == null) return;
            viewer = Camera.main.transform;
        }

        if (!billboardWhileGrabbed && grab != null && grab.isSelected) return;

        // Canvas'ın +Z'si izleyiciden uzağa bakar; kullanıcı panele baktığında
        // yüzeyi görür.
        Vector3 forward = transform.position - viewer.position;
        if (yawOnly) forward.y = 0f;

        if (forward.sqrMagnitude < minViewerDistance * minViewerDistance) return;

        Quaternion target = Quaternion.LookRotation(forward.normalized, Vector3.up);

        transform.rotation = rotationSmoothing > 0f
            ? Quaternion.Slerp(transform.rotation, target,
                               1f - Mathf.Exp(-rotationSmoothing * Time.deltaTime))
            : target;
    }
}
