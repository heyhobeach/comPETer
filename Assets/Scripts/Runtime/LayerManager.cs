using UnityEngine;

public enum PetLayer { Background, Middle, Dynamic }

public class LayerObject : MonoBehaviour
{
    [SerializeField] PetLayer layer = PetLayer.Middle;
    [SerializeField] int orderInLayer = 0;

    void Awake()
    {
        ApplyLayer();
    }

    public void SetLayer(PetLayer newLayer)
    {
        layer = newLayer;
        ApplyLayer();
    }

    void ApplyLayer()
    {
        string layerName = layer.ToString();

        // SpriteRenderer
        foreach (var sr in GetComponentsInChildren<SpriteRenderer>(true))
        {
            sr.sortingLayerName = layerName;
            sr.sortingOrder     = orderInLayer;
        }

        // Canvas (UI도 분리하고 싶다면)
        foreach (var c in GetComponentsInChildren<Canvas>(true))
        {
            c.sortingLayerName = layerName;
            c.sortingOrder     = orderInLayer;
        }
    }
}