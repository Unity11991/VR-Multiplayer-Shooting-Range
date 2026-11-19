using System;
using UnityEngine;

namespace ShootingRange.Core
{
    public enum Handedness
    {
        Left,
        Right
    }

    public enum SurfaceType
    {
        Default,
        Wood,
        Metal,
        Glass,
        Concrete
    }

    public enum HitZone
    {
        None,
        Head,
        Body,
        Limb,
        WeakPoint
    }

    public enum FireMode
    {
        SemiAuto,
        Burst,
        FullAuto
    }

    public enum WeaponType
    {
        Pistol,
        Rifle,
        Shotgun,
        SMG
    }

    public enum HandPoseType
    {
        Open,
        Fist,
        Pinch,
        GripPistol,
        GripRifleFore,
        IndexPoint
    }

    public enum FingerType
    {
        Thumb,
        Index,
        Middle,
        Ring,
        Pinky
    }
}

