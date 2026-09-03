using HarmonyLib;
using InputSystem;
using SuperhotArchipelago.Core;
using UnityEngine;

namespace SuperhotArchipelago.Patches
{
    /// <summary>
    /// SUPERHOT's melee punch and its "click to skip the ending" input are the same button
    /// (left-click). LevelFlowControl.SuperHotSuperHotEnding/EndingClickThrough already wait
    /// a native 1 second after the ending starts before reading that button at all, but a
    /// player still swinging on the killing blow can easily still be clicking (or holding the
    /// button down) past that window, which reads as "click to skip" and yanks them out of the
    /// level a heartbeat after their last kill, before they meant to move on (real user report).
    ///
    /// This zeroes the skip button for a longer, user-tunable window (Config.EndingClickBufferSeconds,
    /// on top of the native 1s) by patching the same two methods TitleCardGatePatch already
    /// hooks, using the same ref-field-injection trick to edit the input before the native
    /// method reads it.
    /// </summary>
    internal static class EndingClickBuffer
    {
        public static void SuppressIfWithinBuffer(ref SHInputGUI.InputData inputData, bool endingStarted, float endingStartedTime)
        {
            if (!endingStarted)
            {
                return;
            }

            if (Time.realtimeSinceStartup - endingStartedTime < Config.EndingClickBufferSeconds.Value)
            {
                inputData.skipButton = SHInput.ButtonState.unpressed;
            }
        }
    }

    [HarmonyPatch(typeof(LevelFlowControl), nameof(LevelFlowControl.SuperHotSuperHotEnding))]
    public static class EndingClickBufferPatch_Ending
    {
        public static void Prefix(ref SHInputGUI.InputData ___inputData, bool ___SuperHotEndingStarted, float ___SuperHotEndingStartedTime) =>
            EndingClickBuffer.SuppressIfWithinBuffer(ref ___inputData, ___SuperHotEndingStarted, ___SuperHotEndingStartedTime);
    }

    [HarmonyPatch(typeof(LevelFlowControl), nameof(LevelFlowControl.SuperHotSuperHotEndingClickThrough))]
    public static class EndingClickBufferPatch_EndingClickThrough
    {
        public static void Prefix(ref SHInputGUI.InputData ___inputData, bool ___SuperHotEndingStarted, float ___SuperHotEndingStartedTime) =>
            EndingClickBuffer.SuppressIfWithinBuffer(ref ___inputData, ___SuperHotEndingStarted, ___SuperHotEndingStartedTime);
    }
}
