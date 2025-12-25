using System;
using System.Collections;
using TMPro;
using UnityEngine;

public class ResponseUI : MonoBehaviour
{
    [SerializeField] private float typingSpeed = 0.05f; // Time between each character
    
    [SerializeField]private TextMeshProUGUI uiText;
    [SerializeField] Transform bg;
    private Coroutine typingCoroutine;
    
  

    private void Start()
    {
        bg.gameObject.SetActive(false);
        SpeechRecognition.Instance.onResponse += OnSpeechResponse;
    }

    private void OnSpeechResponse(SpeechRecognition.PiperResponse response)
    {
        // Stop any previous typing effect
        if (typingCoroutine != null)
        {
            StopCoroutine(typingCoroutine);
        }
        
        // Start typing effect synced with audio
        typingCoroutine = StartCoroutine(TypeText(response.answer));
    }

    private IEnumerator TypeText(string fullText)
    {
        bg.gameObject.SetActive(true);
        uiText.text = "";
        
        // Wait a brief moment for audio to start
        yield return new WaitForSeconds(0.1f);
        
        AudioSource audioSource = SpeechRecognition.Instance.AudioSource;
        
        // Split text into paragraphs (by double newline or single newline)
        string[] paragraphs = fullText.Split(new[] { "\n\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        
        // Trim whitespace from each paragraph
        for (int i = 0; i < paragraphs.Length; i++)
        {
            paragraphs[i] = paragraphs[i].Trim();
        }
        
        if (audioSource != null && audioSource.clip != null)
        {
            // Calculate total typing duration based on audio length
            float audioDuration = audioSource.clip.length;
            int totalCharacters = fullText.Length;
            float characterDelay = audioDuration / totalCharacters;
            
            // Clamp the delay to reasonable bounds
            characterDelay = Mathf.Clamp(characterDelay, 0.02f, 0.15f);
            
            // Type each paragraph one by one
            foreach (string paragraph in paragraphs)
            {
                if (string.IsNullOrWhiteSpace(paragraph))
                    continue;
                
                // Clear previous paragraph
                uiText.text = "";
                
                // Type out current paragraph character by character
                for (int i = 0; i < paragraph.Length; i++)
                {
                    // Stop typing if audio stops playing
                    if (!audioSource.isPlaying)
                    {
                        // Show remaining paragraph immediately
                        uiText.text = paragraph;
                        yield return new WaitForSeconds(0.5f);
                        break;
                    }
                    
                    uiText.text += paragraph[i];
                    yield return new WaitForSeconds(characterDelay);
                }
                
                // Wait a bit before moving to next paragraph (or until audio stops if it's the last one)
                if (audioSource.isPlaying)
                {
                    yield return new WaitForSeconds(0.3f);
                }
            }
        }
        else
        {
            // Fallback: use default typing speed if no audio
            foreach (string paragraph in paragraphs)
            {
                if (string.IsNullOrWhiteSpace(paragraph))
                    continue;
                
                // Clear previous paragraph
                uiText.text = "";
                
                // Type out current paragraph
                for (int i = 0; i < paragraph.Length; i++)
                {
                    uiText.text += paragraph[i];
                    yield return new WaitForSeconds(typingSpeed);
                }
                
                // Wait before next paragraph
                yield return new WaitForSeconds(0.5f);
            }
        }
        
        bg.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (SpeechRecognition.Instance != null)
        {
            SpeechRecognition.Instance.onResponse -= OnSpeechResponse;
        }
    }
}
