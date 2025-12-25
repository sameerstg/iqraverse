using UnityEngine;
using System.Collections;
using System.IO;
using System;
using UnityEngine.Networking;

[RequireComponent(typeof(AudioSource))]
public class SpeechRecognition : MonoBehaviour
{
    public static SpeechRecognition Instance;   
    private AudioClip recordedClip;         // The raw looping buffer from microphone
    private AudioClip lastRecordedClip;     // Trimmed clip of the last successful recording (for replay)

    private bool isRecording = false;
    private int sampleRate = 16000;         // Good for speech, low lag (matches Piper requirement)
    private int maxRecordingSeconds = 20;

    private string microphoneDevice;

    public byte[] recordedAudioBytes;
    public float recordedDuration = 0f;
    public string statusMessage = "Press and hold T to record | Press R to replay last";

    private int recordingStartPosition = 0;

    private AudioSource audioSource;        // Cache for playback
    private string piperApiUrl = "http://localhost:5000/api/voice";  // Piper API endpoint
    public Action<PiperResponse> onResponse;
    
    public AudioSource AudioSource => audioSource;

    private void Awake()
    {
        Instance = this;
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
        statusMessage = "Ready. Press and hold T to record";
    }

    private void Update()
    {
        // Record: Hold T
        if (Input.GetKeyDown(KeyCode.T))
        {
            audioSource.Stop();
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

        //// Send to Piper API: Press S
        //if (Input.GetKeyDown(KeyCode.S))
        //{
        //    SendToPiperAPI();
        //}
    }

    private void StartRecording()
    {
        if (isRecording || string.IsNullOrEmpty(microphoneDevice)) 
        {
            statusMessage = "Already recording!";
            return;
        }

        try
        {
            recordedClip = Microphone.Start(microphoneDevice, true, maxRecordingSeconds, sampleRate);

            if (recordedClip == null)
            {
                statusMessage = "Failed to start recording!";
                Debug.LogError("Microphone.Start returned null");
                return;
            }

            StartCoroutine(CaptureStartPosition());

            isRecording = true;
            statusMessage = "Recording... Speak now! (Hold T)";
            Debug.Log("Recording started.");
        }
        catch (Exception e)
        {
            statusMessage = $"Error: {e.Message}";
            Debug.LogError("Recording error: " + e.Message);
        }
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

        if (recordingStartPosition <= 0)
        {
            Debug.LogWarning("Failed to capture recording start position");
        }
    }

    private void StopRecordingAndProcess()
    {
        if (!isRecording || recordedClip == null) 
        {
            statusMessage = "Not recording!";
            return;
        }

        statusMessage = "Processing audio...";
        isRecording = false;

        Microphone.End(microphoneDevice);

        StartCoroutine(ProcessRecordedAudioAndSend());
    }

    private IEnumerator ProcessRecordedAudioAndSend()
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

        Debug.Log($"Success! Recorded {recordedDuration:F2} seconds. Sending to API...");

        Cleanup();

        // Send to API immediately after processing
        SendToPiperAPI();
    }

    private void ReplayLastRecording()
    {
        if (lastRecordedClip == null)
        {
            statusMessage = "No recording to replay yet";
            Debug.Log("Nothing to replay.");
            return;
        }

        try
        {
            audioSource.Stop();
            audioSource.clip = lastRecordedClip;
            audioSource.Play();

            statusMessage = $"Replaying {recordedDuration:F2}s recording...";
            Debug.Log("Replaying last recording.");
        }
        catch (Exception e)
        {
            statusMessage = $"Playback error: {e.Message}";
            Debug.LogError("Playback error: " + e.Message);
        }
    }

    /// <summary>
    /// Sends the recorded audio to Piper voice-to-voice API
    /// </summary>
    private void SendToPiperAPI()
    {
        if (recordedAudioBytes == null || recordedAudioBytes.Length == 0)
        {
            statusMessage = "No audio recorded! Press T to record first.";
            Debug.LogWarning("No recorded audio to send");
            return;
        }

        statusMessage = "Sending to Piper API...";
        StartCoroutine(SendAudioToPiper(recordedAudioBytes));
    }

    private IEnumerator SendAudioToPiper(byte[] audioData)
    {
        // Create form data with audio file
        WWWForm form = new WWWForm();
        form.AddBinaryData("audio", audioData, "question.wav", "audio/wav");

        UnityWebRequest request = UnityWebRequest.Post(piperApiUrl, form);
        request.downloadHandler = new DownloadHandlerBuffer();

        statusMessage = "Waiting for Piper response...";

        yield return request.SendWebRequest();

        // Handle response after the yield
        try
        {
            if (request.result == UnityWebRequest.Result.Success)
            {
                string responseText = request.downloadHandler.text;
                Debug.Log("Piper response: " + responseText);

                // Parse JSON response
                PiperResponse response = JsonUtility.FromJson<PiperResponse>(responseText);

                statusMessage = $"Q: {response.question}\nA: {response.answer}";
                Debug.Log($"Question: {response.question}");
                Debug.Log($"Answer: {response.answer}");
                onResponse?.Invoke(response);
                // Decode and play response audio
                if (!string.IsNullOrEmpty(response.audio))
                {
                    byte[] responseAudioBytes = System.Convert.FromBase64String(response.audio);
                    PlayAudioFromBytes(responseAudioBytes);
                }
            }
            else
            {
                string errorText = request.downloadHandler.text;
                Debug.LogError($"Piper API error: {request.error}\nResponse: {errorText}");

                try
                {
                    PiperErrorResponse errorResponse = JsonUtility.FromJson<PiperErrorResponse>(errorText);
                    statusMessage = $"Error: {errorResponse.error}";
                }
                catch
                {
                    statusMessage = $"Error: {request.error}";
                }
            }
        }
        catch (Exception e)
        {
            statusMessage = $"Connection error: {e.Message}";
            Debug.LogError("API call error: " + e.Message);
        }
        finally
        {
            request.Dispose();
        }
    }

    /// <summary>
    /// Plays audio from raw WAV bytes
    /// </summary>
    private void PlayAudioFromBytes(byte[] wavBytes)
    {
        try
        {
            AudioClip responseClip = SavWav.LoadWavFromBytes(wavBytes, "PiperResponse");
            if (responseClip != null)
            {
                audioSource.Stop();
                audioSource.clip = responseClip;
                audioSource.Play();
                statusMessage += "\nPlaying Piper response...";
                Debug.Log("Playing Piper response audio");
            }
        }
        catch (Exception e)
        {
            statusMessage += $"\nFailed to play response: {e.Message}";
            Debug.LogError("Audio playback error: " + e.Message);
        }
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
        if (isRecording && !string.IsNullOrEmpty(microphoneDevice))
        {
            try
            {
                Microphone.End(microphoneDevice);
            }
            catch { }
        }
    }

    // JSON response structures for Piper API
    [System.Serializable]
    public class PiperResponse
    {
        public string question;
        public string answer;
        public string audio;  // Base64 encoded WAV
    }

    [System.Serializable]
    public class PiperErrorResponse
    {
        public string error;
    }
}

/// <summary>
/// WAV file handling utilities
/// </summary>
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
            System.Array.Copy(fullBuffer, startOffsetInterleaved, recordedSamples, 0, totalRecordedSamples);
        }
        else
        {
            int firstPart = bufferTotalSamples - startOffsetInterleaved;
            int secondPart = totalRecordedSamples - firstPart;

            System.Array.Copy(fullBuffer, startOffsetInterleaved, recordedSamples, 0, firstPart);
            System.Array.Copy(fullBuffer, 0, recordedSamples, firstPart, secondPart);
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
        UnityEngine.Object.Destroy(tempTrimmed);
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

    /// <summary>
    /// Load WAV audio from raw bytes
    /// </summary>
    public static AudioClip LoadWavFromBytes(byte[] wavBytes, string clipName = "WAVClip")
    {
        try
        {
            using (MemoryStream stream = new MemoryStream(wavBytes))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                // Read WAV header
                int riffHeader = reader.ReadInt32();
                if (riffHeader != 0x46464952) // RIFF
                    throw new System.Exception("Invalid WAV file - missing RIFF header");

                int fileSize = reader.ReadInt32();
                int waveHeader = reader.ReadInt32();
                if (waveHeader != 0x45564157) // WAVE
                    throw new System.Exception("Invalid WAV file - missing WAVE header");

                int fmtHeader = reader.ReadInt32();
                if (fmtHeader != 0x20746D66) // fmt
                    throw new System.Exception("Invalid WAV file - missing fmt header");

                int fmtSize = reader.ReadInt32();
                short audioFormat = reader.ReadInt16();
                short numChannels = reader.ReadInt16();
                int sampleRate = reader.ReadInt32();
                int byteRate = reader.ReadInt32();
                short blockAlign = reader.ReadInt16();
                short bitsPerSample = reader.ReadInt16();

                // Find data chunk
                int dataHeader = reader.ReadInt32();
                while (dataHeader != 0x61746164) // data
                {
                    int chunkSize = reader.ReadInt32();
                    reader.ReadBytes(chunkSize);
                    if (reader.BaseStream.Position >= reader.BaseStream.Length)
                        throw new System.Exception("Invalid WAV file - no data chunk");
                    dataHeader = reader.ReadInt32();
                }

                int dataSize = reader.ReadInt32();
                byte[] pcmData = reader.ReadBytes(dataSize);

                // Convert PCM bytes to float samples
                int sampleCount = dataSize / 2;
                float[] samples = new float[sampleCount];
                for (int i = 0; i < sampleCount; i++)
                {
                    short sample = (short)((pcmData[i * 2 + 1] << 8) | pcmData[i * 2]);
                    samples[i] = sample / 32768f;
                }

                // Create AudioClip
                AudioClip clip = AudioClip.Create(clipName, sampleCount / numChannels, numChannels, sampleRate, false);
                clip.SetData(samples, 0);

                return clip;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError("Error loading WAV: " + e.Message);
            return null;
        }
    }
}