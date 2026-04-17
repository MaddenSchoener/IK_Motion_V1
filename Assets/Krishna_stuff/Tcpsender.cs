using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using UnityEngine;

public class TCPSender : MonoBehaviour
{
    // Maps joint names to hardware channel IDs (JointID enum in robot_components.hpp)
    // R1_SHOULDER_PITCH=1, R2_SHOULDER_ROLL=2, R3_ELBOW_HINGE=3
    // L1_SHOULDER_PITCH=4, L2_SHOULDER_ROLL=5, L3_ELBOW_HINGE=6
    private static readonly Dictionary<string, uint> JointChannelMap = new()
    {
        { "R1", 1 }, { "R2", 2 }, { "R3", 3 },
        { "L1", 4 }, { "L2", 5 }, { "L3", 6 }
    };

    private const uint COMP_SERVO = 1; // ComponentID enum in robot_components.hpp

    [SerializeField] private string ipAddress = "127.0.0.1";
    [SerializeField] private string csvOutputFolder = "ServoCommands"; // Must match TwoBoneIKSolver's csvOutputFolder field
    private readonly int port = 8080;

    private TcpClient     _client;
    private NetworkStream _stream;
    private uint          _frameCounter = 0;
    private DateTime      _lastReadTime;
    private string        _filePath;

    // -----------------------------------------------------------------
    // Unity lifecycle
    // -----------------------------------------------------------------

    void Start()
    {
        // Mirrors the path TwoBoneIKSolver.WriteCSV() constructs
        string folderPath = Path.Combine(Application.dataPath, "..", csvOutputFolder);
        _filePath = Path.Combine(folderPath, "servo_commands.csv");

        ConnectToPi();
    }

    void OnDestroy()
    {
        Disconnect();
    }

    // -----------------------------------------------------------------
    // Poll for file changes each frame
    // -----------------------------------------------------------------

    void Update()
    {
        if (!File.Exists(_filePath)) return;

        DateTime currentTime = File.GetLastWriteTime(_filePath);
        if (currentTime != _lastReadTime)
        {
            _lastReadTime = currentTime;
            SendData();
        }
    }

    // -----------------------------------------------------------------
    // Read CSV and send binary packets to the Pi
    // -----------------------------------------------------------------

    private void SendData()
    {
        if (!IsConnected())
        {
            Debug.LogWarning("[TCPSender] Not connected — attempting reconnect...");
            ConnectToPi();
            if (!IsConnected()) return;
        }

        // Read CSV — may briefly race with TwoBoneIKSolver's StreamWriter,
        // retry once if the file is locked
        string csv;
        try
        {
            csv = File.ReadAllText(_filePath);
        }
        catch (IOException)
        {
            System.Threading.Thread.Sleep(5);
            try { csv = File.ReadAllText(_filePath); }
            catch (Exception e) { Debug.LogError($"[TCPSender] CSV read failed: {e.Message}"); return; }
        }

        // Parse CSV rows into groups keyed by instruction number
        // CSV format: groupID,jointName,angle,speed  (speed is ignored — no field in ServoData)
        var groups = new SortedDictionary<int, List<(float angle, uint channel)>>();

        foreach (string line in csv.Split('\n'))
        {
            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            string[] parts = trimmed.Split(',');
            if (parts.Length < 3) continue;

            if (!int.TryParse(parts[0].Trim(), out int groupID))          continue;
            string jointName = parts[1].Trim().ToUpper();
            if (!float.TryParse(parts[2].Trim(), out float angle))        continue;
            if (!JointChannelMap.TryGetValue(jointName, out uint channel)) continue;

            if (!groups.ContainsKey(groupID))
                groups[groupID] = new List<(float, uint)>();

            groups[groupID].Add((angle, channel));
        }

        if (groups.Count == 0) return;

        try
        {
            // BinaryWriter sends raw bytes matching what recv() reads into
            // PacketHeader and ServoData structs on the Pi.
            // leaveOpen: true keeps the NetworkStream alive after the using block.
            using BinaryWriter writer = new BinaryWriter(_stream, System.Text.Encoding.UTF8, leaveOpen: true);

            foreach (var (groupID, servos) in groups)
            {
                // PacketHeader — 12 bytes
                // struct PacketHeader { uint32_t group_id; uint32_t component_target; uint32_t num_commands; }
                writer.Write((uint)groupID);        // which parallel instruction group
                writer.Write(COMP_SERVO);           // routes packet to ServoSystem on the Pi
                writer.Write((uint)servos.Count);   // how many ServoData structs follow

                // ServoData[] — 8 bytes each
                // struct ServoData { float angle; uint32_t channel; }
                foreach (var (angle, channel) in servos)
                {
                    writer.Write(angle);    // target angle in degrees
                    writer.Write(channel);  // PWM channel on the Pi's PCA9685
                }

                _frameCounter++;
            }

            writer.Flush();
            Debug.Log($"[TCPSender] Sent {groups.Count} groups | Frame {_frameCounter}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[TCPSender] Send failed: {e.Message}");
            Disconnect();
        }
    }

    // -----------------------------------------------------------------
    // Connection helpers
    // -----------------------------------------------------------------

    private void ConnectToPi()
    {
        try
        {
            _client = new TcpClient();
            _client.Connect(ipAddress, port);
            _stream = _client.GetStream();
            Debug.Log("[TCPSender] Connected to Pi.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[TCPSender] Connection failed: {e.Message}");
        }
    }

    private void Disconnect()
    {
        _stream?.Close();
        _client?.Close();
    }

    private bool IsConnected() => _client != null && _client.Connected;
}