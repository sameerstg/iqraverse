using UnityEngine;
using System.Collections;
using System.IO;
using System; // For Array.Copy

[RequireComponent(typeof(AudioSource))]
public class SpeechRecognition : MonoBehaviour
{
    private AudioClip recordedClip;         // The raw looping buffer from microphone
    private AudioClip lastRecordedClip;     // Trimmed clip of the last successful recording (for replay)

    private bool isRecording = false;
    private int sampleRate = 16000;         // Good for speech, low lag
    private int maxRecordingSeconds = 20;

    private string microphoneDevice;

    public byte[] recordedAudioBytes;
    public float recordedDuration = 0f;
    public string statusMessage = "Press and hold T to record | Press R to replay last";

    private int recordingStartPosition = 0;

    private AudioSource audioSource; // Cache for playback

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
    }

    private void Start()
    {
        if (Microphone.devices.Length == 0)
        {
            statusMessage = "Error: No microphone detected!";
            Debug.LogError("No microphone found.");
            return;
        }

        microphoneDevice = Microphone.devices[0];
        Debug.Log("Using microphone: " + microphoneDevice);
    }

    private void Update()
    {
        // Record: Hold T
        if (Input.GetKeyDown(KeyCode.T))
        {
            StartRecording();
        }

        if (Input.GetKeyUp(KeyCode.T) && isRecording)
        {
            StopRecordingAndProcess();
        }

        // Replay: Press R
        if (Input.GetKeyDown(KeyCode.R))
        {
            ReplayLastRecording();
        }
    }

    private void StartRecording()
    {
        if (isRecording || string.IsNullOrEmpty(microphoneDevice)) return;

        recordedClip = Microphone.Start(microphoneDevice, true, maxRecordingSeconds, sampleRate);

        if (recordedClip == null)
        {
            statusMessage = "Failed to start recording!";
            return;
        }

        StartCoroutine(CaptureStartPosition());

        isRecording = true;
        statusMessage = "Recording... Speak now! (Hold T)";
        Debug.Log("Recording started.");
    }

    private IEnumerator CaptureStartPosition()
    {
        yield return new WaitForSeconds(0.1f);
        recordingStartPosition = Microphone.GetPosition(microphoneDevice);

        float timeout = Time.time + 0.5f;
        while (recordingStartPosition <= 0 && Time.time < timeout)
        {
            yield return null;
            recordingStartPosition = Microphone.GetPosition(microphoneDevice);
        }
    }

    private void StopRecordingAndProcess()
    {
        if (!isRecording || recordedClip == null) return;

        statusMessage = "Processing audio...";
        isRecording = false;

        Microphone.End(microphoneDevice);

        StartCoroutine(ProcessRecordedAudio());
    }

    private IEnumerator ProcessRecordedAudio()
    {
        yield return null;

        int currentPosition = Microphone.GetPosition(microphoneDevice);

        if (currentPosition <= 0)
        {
            float timeout = Time.time + 0.5f;
            while (Time.time < timeout)
            {
                currentPosition = Microphone.GetPosition(microphoneDevice);
                if (currentPosition > 0) break;
                yield return null;
            }
        }

        int bufferSamples = recordedClip.samples;
        int recordedSamples;

        if (currentPosition >= recordingStartPosition)
        {
            recordedSamples = currentPosition - recordingStartPosition;
        }
        else
        {
            recordedSamples = (currentPosition + bufferSamples) - recordingStartPosition;
        }

        if (recordedSamples <= 500) // ~30ms minimum
        {
            statusMessage = "No audio captured (too short)";
            Debug.LogWarning("No meaningful samples recorded.");
            Cleanup();
            yield break;
        }

        recordedDuration = recordedSamples / (float)sampleRate;

        // Convert directly to WAV bytes (fast)
        recordedAudioBytes = SavWav.SaveToWavBytesDirect(recordedClip, recordingStartPosition, recordedSamples);

        // Create a trimmed clip for clean replay
        if (lastRecordedClip != null) Destroy(lastRecordedClip);
        lastRecordedClip = SavWav.CreateTrimmedClip(recordedClip, recordingStartPosition, recordedSamples);

        statusMessage = $"Recorded {recordedDuration:F2}s | Press R to replay";

        Debug.Log($"Success! Recorded {recordedDuration:F2} seconds. Press R to replay.");

        Cleanup();
    }

    private void ReplayLastRecording()
    {
        if (lastRecordedClip == null)
        {
            statusMessage = "No recording to replay yet";
            Debug.Log("Nothing to replay.");
            return;
        }

        audioSource.Stop();
        audioSource.clip = lastRecordedClip;
        audioSource.Play();

        statusMessage = $"Replaying {recordedDuration:F2}s recording...";
        Debug.Log("Replaying last recording.");
    }

    private void Cleanup()
    {
        if (recordedClip != null)
        {
            Destroy(recordedClip);
            recordedClip = null;
        }
    }

    private void OnDestroy()
    {
        Cleanup();
        if (lastRecordedClip != null) Destroy(lastRecordedClip);
        if (isRecording) Microphone.End(microphoneDevice);
    }
}

// Updated SavWav with helper for trimmed clip (for playback)
public static class SavWav
{
    // Creates a new AudioClip with only the recorded portion (handles wrap-around)
    public static AudioClip CreateTrimmedClip(AudioClip original, int startOffsetSamples, int recordedSampleCount)
    {
        int channels = original.channels;
        int totalRecordedSamples = recordedSampleCount * channels;
        int bufferTotalSamples = original.samples * channels;

        float[] fullBuffer = new float[bufferTotalSamples];
        original.GetData(fullBuffer, 0);

        float[] recordedSamples = new float[totalRecordedSamples];
        int startOffsetInterleaved = startOffsetSamples * channels;

        if (startOffsetInterleaved + totalRecordedSamples <= bufferTotalSamples)
        {
            Array.Copy(fullBuffer, startOffsetInterleaved, recordedSamples, 0, totalRecordedSamples);
        }
        else
        {
            int firstPart = bufferTotalSamples - startOffsetInterleaved;
            int secondPart = totalRecordedSamples - firstPart;

            Array.Copy(fullBuffer, startOffsetInterleaved, recordedSamples, 0, firstPart);
            Array.Copy(fullBuffer, 0, recordedSamples, firstPart, secondPart);
        }

        AudioClip trimmed = AudioClip.Create("LastRecording", recordedSampleCount, channels, original.frequency, false);
        trimmed.SetData(recordedSamples, 0);

        return trimmed;
    }

    // Direct WAV export (fast, no extra clip)
  public static byte[] SaveToWavBytesDirect(AudioClip clip, int startOffsetSamples, int recordedSampleCount)
{
    AudioClip tempTrimmed = CreateTrimmedClip(clip, startOffsetSamples, recordedSampleCount);
    byte[] wav = SaveToWavBytes(tempTrimmed);
    UnityEngine.Object.Destroy(tempTrimmed); // ← Fixed line
    return wav;
}

    // Standard full-clip WAV export
    public static byte[] SaveToWavBytes(AudioClip clip)
    {
        int channels = clip.channels;
        int samples = clip.samples;
        float[] floatSamples = new float[samples * channels];
        clip.GetData(floatSamples, 0);

        byte[] pcm = new byte[floatSamples.Length * 2];
        for (int i = 0; i < floatSamples.Length; i++)
        {
            short value = (short)(Mathf.Clamp(floatSamples[i], -1f, 1f) * 32767f);
            pcm[i * 2]     = (byte)(value & 0xFF);
            pcm[i * 2 + 1] = (byte)((value >> 8) & 0xFF);
        }

        using (MemoryStream stream = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            writer.Write(0x46464952); // RIFF
            writer.Write(36 + pcm.Length);
            writer.Write(0x45564157); // WAVE
            writer.Write(0x20746D66); // fmt 
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)channels);
            writer.Write(clip.frequency);
            writer.Write(clip.frequency * channels * 2);
            writer.Write((short)(channels * 2));
            writer.Write((short)16);
            writer.Write(0x61746164); // data
            writer.Write(pcm.Length);
            writer.Write(pcm);

            return stream.ToArray();
        }
    }
}