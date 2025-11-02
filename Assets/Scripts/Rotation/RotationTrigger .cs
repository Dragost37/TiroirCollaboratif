using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections;

public class RotationTrigger : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    [Tooltip("Assign your UI prefab in the Inspector")]
    public GameObject rotationUIPrefab;

    [Tooltip("How long to hold for (seconds)")]
    public float longPressDuration = 1.5f;

    [Header("Mouse Emulation (for editor / mouse platforms)")]
    [Tooltip("Enable emulation of a two-finger gesture using the mouse")]
    public bool enableMouseEmulation = true;

    [Tooltip("If true, require both left and right mouse buttons to be held. If false, allow left mouse + modifier key.")]
    public bool requireBothMouseButtons = true;

    [Tooltip("Modifier key to use when not requiring both mouse buttons (e.g. Shift, Control, Alt)")]
    public KeyCode mouseEmulationModifier = KeyCode.LeftShift;

    private Coroutine longPressCoroutine;
    private GameObject spawnedUI;

    public void OnPointerDown(PointerEventData eventData)
    {
        // Only start one long-press coroutine at a time and only if no UI is spawned yet
        if (IsTwoFingerActive() && spawnedUI == null && longPressCoroutine == null)
        {
            longPressCoroutine = StartCoroutine(LongPressCheck());
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (longPressCoroutine != null)
        {
            StopCoroutine(longPressCoroutine);
            longPressCoroutine = null;
        }
    }

    private IEnumerator LongPressCheck()
    {
        float elapsed = 0f;

        while (elapsed < longPressDuration)
        {
            if (!IsTwoFingerActive())
            {
                longPressCoroutine = null;
                yield break;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        if (IsTwoFingerActive())
        {
            Debug.Log("2-Finger Long Press Detected (or mouse-emulated)!");
            if (spawnedUI == null)
            {
                SpawnRotationUI();
            }
            else
            {
                Debug.Log("Rotation UI already spawned — skipping duplicate.");
            }
        }

        longPressCoroutine = null;
    }

    private bool IsTwoFingerActive()
    {
        if (Input.touchCount >= 2)
        {
            int concurrentTouchesOnObject = 0;
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch t = Input.GetTouch(i);
                if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
                    continue;

                if (IsTouchOverGameObject(t))
                    concurrentTouchesOnObject++;
            }

            if (concurrentTouchesOnObject == 2)
                return true;
        }

        if (!enableMouseEmulation) return false;
        if (requireBothMouseButtons)
        {
            if (Input.GetMouseButton(0) && Input.GetMouseButton(1))
                return true;
        }
        else
        {
            if (Input.GetMouseButton(0) && (Input.GetKey(mouseEmulationModifier)))
                return true;
        }
        return false;
    }

    private bool IsTouchOverGameObject(Touch touch)
    {
        if (EventSystem.current != null)
        {
            PointerEventData pointerData = new PointerEventData(EventSystem.current);
            pointerData.position = touch.position;
            var results = new System.Collections.Generic.List<RaycastResult>();
            EventSystem.current.RaycastAll(pointerData, results);
            foreach (var r in results)
            {
                if (r.gameObject == this.gameObject || r.gameObject.transform.IsChildOf(this.transform))
                    return true;
            }
        }

        Camera cam = Camera.main;
        if (cam != null)
        {
            Ray ray = cam.ScreenPointToRay(touch.position);
            RaycastHit hit;
            if (Physics.Raycast(ray, out hit))
            {
                if (hit.transform == this.transform || hit.transform.IsChildOf(this.transform))
                    return true;
            }
        }

        return false;
    }

    private void SpawnRotationUI()
    {
        if (rotationUIPrefab == null) return;
        Canvas mainCanvas = FindObjectOfType<Canvas>();
        if (mainCanvas == null)
        {
            Debug.LogError("No Canvas found in scene!");
            return;
        }

        spawnedUI = Instantiate(rotationUIPrefab, mainCanvas.transform);

        RotationController controller = spawnedUI.GetComponent<RotationController>();
        if (controller != null)
        {
            controller.SetTargetObject(this.transform);
        }
    }

    public void DismissSpawnedUI()
    {
        if (spawnedUI != null)
        {
            Destroy(spawnedUI);
            spawnedUI = null;
        }
    }
}