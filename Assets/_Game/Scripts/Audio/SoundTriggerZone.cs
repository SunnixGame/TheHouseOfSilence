using UnityEngine;

[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(AudioSource))]
public class SoundTriggerZone : MonoBehaviour
{
    [Header("Audio")]
    [SerializeField] private AudioClip soundToPlay;

    [Header("Options")]
    [SerializeField] private bool playOnce = true;
    [SerializeField] private bool stopWhenExit = false;

    private AudioSource audioSource;
    private bool hasPlayed = false;

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        audioSource.playOnAwake = false;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player"))
            return;

        if (playOnce && hasPlayed)
            return;

        if (soundToPlay != null)
        {
            audioSource.clip = soundToPlay;
            audioSource.Play();
            hasPlayed = true;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player"))
            return;

        if (stopWhenExit)
        {
            audioSource.Stop();
        }
    }
}