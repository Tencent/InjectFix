using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VMBehaviourScript : MonoBehaviour
{
    public IMonoBehaviour VMMonoBehaviour { get;set; }

    void Start()
    {
        if (VMMonoBehaviour != null)
        {
            VMMonoBehaviour.Start();
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (VMMonoBehaviour != null)
        {
            VMMonoBehaviour.Update();
        }
    }
}
