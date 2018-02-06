using System;
using linerider.Game;

namespace linerider.Drawing
{
    public class DrawOptions
    {
        public bool Playback = false;
        public bool Paused = false;
        public bool LineColors = true;
        public bool GravityWells = false;
        public bool NightMode=false;
        public KnobState KnobState = 0;
        public float Blend = 1;
        public Rider Rider;
        public bool ShowContactLines = false;
        public bool ShowMomentumVectors = false;
        public int Iteration = 6;
        public float Zoom;
        public bool IsRunning
        {
            get
            {
                return Playback && !Paused;
            }
        }
    }
}