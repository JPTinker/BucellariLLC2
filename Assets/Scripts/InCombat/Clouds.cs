using UnityEngine;

public class Clouds : MonoBehaviour
{
    float cloudMovement = 30.0f;
    public Material cloudMaterial;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        //cloudMovement = this.gameObject.GetComponent<> //Random.Range(0.1f, 0.5f);
    }

    // Update is called once per frame
    void Update()
    {
        cloudMovement += Time.deltaTime * 0.1f;
        cloudMaterial.SetFloat("_Scale", cloudMovement);
    }
}
