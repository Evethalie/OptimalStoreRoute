using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneSwitcher : MonoBehaviour
{
    public void GoTo(int sceneIndex)
    {
        SceneManager.LoadScene(sceneIndex);
    }
}
