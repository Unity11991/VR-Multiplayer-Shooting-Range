using System;
using System.IO;
using UnityEngine;

namespace ShootingRange.Replay
{
    /// <summary>
    /// Compact quantized transform representation for memory-efficient 30-second rolling replay.
    /// Stores positions at 1mm precision (short) and rotations with quantized Euler or smallest-three.
    /// </summary>
    public struct QuantizedPose
    {
        public short PosX;
        public short PosY;
        public short PosZ;
        public sbyte RotPitch; // -180 to 180 mapped to -128 to 127
        public sbyte RotYaw;
        public sbyte RotRoll;

        public static QuantizedPose Quantize(Vector3 pos, Quaternion rot, Vector3 origin)
        {
            Vector3 rel = pos - origin;
            Vector3 euler = rot.eulerAngles;

            return new QuantizedPose
            {
                PosX = (short)Mathf.Clamp(rel.x * 1000f, short.MinValue, short.MaxValue),
                PosY = (short)Mathf.Clamp(rel.y * 1000f, short.MinValue, short.MaxValue),
                PosZ = (short)Mathf.Clamp(rel.z * 1000f, short.MinValue, short.MaxValue),
                RotPitch = (sbyte)Mathf.Clamp(Mathf.DeltaAngle(0, euler.x) * (127f / 180f), -128, 127),
                RotYaw = (sbyte)Mathf.Clamp(Mathf.DeltaAngle(0, euler.y) * (127f / 180f), -128, 127),
                RotRoll = (sbyte)Mathf.Clamp(Mathf.DeltaAngle(0, euler.z) * (127f / 180f), -128, 127)
            };
        }

        public void Dequantize(Vector3 origin, out Vector3 pos, out Quaternion rot)
        {
            pos = origin + new Vector3(PosX * 0.001f, PosY * 0.001f, PosZ * 0.001f);
            float pitch = RotPitch * (180f / 127f);
            float yaw = RotYaw * (180f / 127f);
            float roll = RotRoll * (180f / 127f);
            rot = Quaternion.Euler(pitch, yaw, roll);
        }

        public void WriteBinary(BinaryWriter writer)
        {
            writer.Write(PosX);
            writer.Write(PosY);
            writer.Write(PosZ);
            writer.Write(RotPitch);
            writer.Write(RotYaw);
            writer.Write(RotRoll);
        }

        public static QuantizedPose ReadBinary(BinaryReader reader)
        {
            return new QuantizedPose
            {
                PosX = reader.ReadInt16(),
                PosY = reader.ReadInt16(),
                PosZ = reader.ReadInt16(),
                RotPitch = reader.ReadSByte(),
                RotYaw = reader.ReadSByte(),
                RotRoll = reader.ReadSByte()
            };
        }
    }

    public struct ReplayShotEvent
    {
        public float Timestamp;
        public QuantizedPose MuzzlePose;
        public int WeaponId;
    }

    public struct ReplayHitEvent
    {
        public float Timestamp;
        public int TargetId;
        public byte HitZoneByte;
        public QuantizedPose HitPointPose;
    }

    public class ReplayRecordedFrame
    {
        public float Timestamp;
        public QuantizedPose Player1Head;
        public QuantizedPose Player1LeftHand;
        public QuantizedPose Player1RightHand;
        public QuantizedPose Player2Head;
        public QuantizedPose WeaponPose;
    }
}

