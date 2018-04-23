using System;
using System.Windows.Forms;
using FMOD;

// TODO Double-check channelgroup argument in new FMOD Studio API system.playSound method

namespace AssetStudio
{
    public partial class AssetStudioForm
    {
        private FMOD.System system;
        private Sound sound;
        private Channel channel;
        private SoundGroup masterSoundGroup;
        //private ChannelGroup channelGroup;
        private MODE loopMode = MODE.LOOP_OFF;
        private uint FMODlenms;
        private float FMODVolume = 0.8f;
        private float FMODfrequency;

        private bool ERRCHECK(RESULT result)
        {
            if (result != RESULT.OK)
            {
                FMODreset();
                StatusStripUpdate("FMOD error! " + result + " - " + Error.String(result));
                return true;
            }

            return false;
        }

        private void FMODinit()
        {
            FMODreset();

            RESULT result = Factory.System_Create(out system);
            if (ERRCHECK(result))
                return;

            uint version;
            result = system.getVersion(out version);
            ERRCHECK(result);
            if (version < VERSION.number)
            {
                MessageBox.Show(
                    "Error!  You are using an old version of FMOD " + version.ToString("X") + ".  " +
                    "This program requires " + VERSION.number.ToString("X") + ".");
                Application.Exit();
            }

            result = system.init(1, INITFLAGS.NORMAL, (IntPtr)null);
            if (ERRCHECK(result))
                return;

            result = system.getMasterSoundGroup(out masterSoundGroup);
            if (ERRCHECK(result))
                return;

            result = masterSoundGroup.setVolume(FMODVolume);
            ERRCHECK(result);
        }

        private void FMODreset()
        {
            timer.Stop();
            FMODprogressBar.Value = 0;
            FMODtimerLabel.Text = "0:00.0 / 0:00.0";
            FMODstatusLabel.Text = "Stopped";
            FMODinfoLabel.Text = "";

            if (sound != null)
            {
                RESULT result = sound.release();
                if (result != RESULT.OK)
                {
                    StatusStripUpdate("FMOD error! " + result + " - " + Error.String(result));
                }
                sound = null;
            }
        }

        private void FMODplayButton_Click(object sender, EventArgs e)
        {
            if (sound != null && channel != null)
            {
                timer.Start();
                bool playing;
                RESULT result = channel.isPlaying(out playing);
                if (result != RESULT.OK && result != RESULT.ERR_INVALID_HANDLE)
                {
                    if (ERRCHECK(result))
                        return;
                }

                if (playing)
                {
                    result = channel.stop();
                    if (ERRCHECK(result))
                        return;

                    result = system.playSound(sound, null, false, out channel);
                    if (ERRCHECK(result))
                        return;


                    FMODpauseButton.Text = "Pause";
                }
                else
                {
                    result = system.playSound(sound, null, false, out channel);
                    if (ERRCHECK(result))
                        return;

                    FMODstatusLabel.Text = "Playing";
                    //FMODinfoLabel.Text = FMODfrequency.ToString();

                    if (FMODprogressBar.Value > 0)
                    {
                        uint newms = FMODlenms / 1000 * (uint) FMODprogressBar.Value;

                        result = channel.setPosition(newms, TIMEUNIT.MS);
                        if (result != RESULT.OK && result != RESULT.ERR_INVALID_HANDLE)
                        {
                            ERRCHECK(result);
                        }
                    }
                }
            }
        }

        private void FMODpauseButton_Click(object sender, EventArgs e)
        {
            if (sound != null && channel != null)
            {
                bool playing;
                RESULT result = channel.isPlaying(out playing);
                if (result != RESULT.OK && result != RESULT.ERR_INVALID_HANDLE)
                {
                    if (ERRCHECK(result))
                        return;
                }

                if (playing)
                {
                    bool paused;
                    result = channel.getPaused(out paused);
                    if (ERRCHECK(result))
                        return;

                    result = channel.setPaused(!paused);
                    if (ERRCHECK(result))
                        return;

                    if (paused)
                    {
                        FMODstatusLabel.Text = "Playing";
                        FMODpauseButton.Text = "Pause";
                        timer.Start();
                    }
                    else
                    {
                        FMODstatusLabel.Text = "Paused";
                        FMODpauseButton.Text = "Resume";
                        timer.Stop();
                    }
                }
            }
        }

        private void FMODstopButton_Click(object sender, EventArgs e)
        {
            if (channel != null)
            {
                bool playing;
                RESULT result = channel.isPlaying(out playing);
                if (result != RESULT.OK && result != RESULT.ERR_INVALID_HANDLE)
                {
                    if (ERRCHECK(result))
                        return;
                }

                if (playing)
                {
                    result = channel.stop();
                    if (ERRCHECK(result))
                        return;

                    //channel = null;
                    //don't FMODreset, it will nullify the sound
                    timer.Stop();
                    FMODprogressBar.Value = 0;
                    FMODtimerLabel.Text = "0:00.0 / 0:00.0";
                    FMODstatusLabel.Text = "Stopped";
                    FMODpauseButton.Text = "Pause";
                }
            }
        }

        private void FMODloopButton_CheckedChanged(object sender, EventArgs e)
        {
            RESULT result;

            if (FMODloopButton.Checked)
            {
                loopMode = MODE.LOOP_NORMAL;
            }
            else
            {
                loopMode = MODE.LOOP_OFF;
            }

            if (sound != null)
            {
                result = sound.setMode(loopMode);
                if (ERRCHECK(result))
                    return;
            }

            if (channel != null)
            {
                bool playing;
                result = channel.isPlaying(out playing);
                if (result != RESULT.OK && result != RESULT.ERR_INVALID_HANDLE)
                {
                    if (ERRCHECK(result))
                        return;
                }

                bool paused;
                result = channel.getPaused(out paused);
                if (result != RESULT.OK && result != RESULT.ERR_INVALID_HANDLE)
                {
                    if (ERRCHECK(result))
                        return;
                }

                if (playing || paused)
                {
                    result = channel.setMode(loopMode);
                    ERRCHECK(result);
                }
            }
        }

        private void FMODvolumeBar_ValueChanged(object sender, EventArgs e)
        {
            FMODVolume = Convert.ToSingle(FMODvolumeBar.Value) / 10;

            RESULT result = masterSoundGroup.setVolume(FMODVolume);
            ERRCHECK(result);
        }

        private void FMODprogressBar_Scroll(object sender, EventArgs e)
        {
            if (channel != null)
            {
                uint newms = FMODlenms / 1000 * (uint)FMODprogressBar.Value;
                FMODtimerLabel.Text =
                    newms / 1000 / 60 + ":" + newms / 1000 % 60 + "." + newms / 10 % 100 + "/"+
                    FMODlenms / 1000 / 60 + ":" + FMODlenms / 1000 % 60 + "." + FMODlenms / 10 % 100;
            }
        }

        private void FMODprogressBar_MouseDown(object sender, MouseEventArgs e)
        {
            timer.Stop();
        }

        private void FMODprogressBar_MouseUp(object sender, MouseEventArgs e)
        {
            if (channel != null)
            {
                uint newms = FMODlenms / 1000 * (uint)FMODprogressBar.Value;

                RESULT result = channel.setPosition(newms, TIMEUNIT.MS);
                if (result != RESULT.OK && result != RESULT.ERR_INVALID_HANDLE)
                {
                    if (ERRCHECK(result))
                        return;
                }

                bool playing;
                result = channel.isPlaying(out playing);
                if (result != RESULT.OK && result != RESULT.ERR_INVALID_HANDLE)
                {
                    if (ERRCHECK(result))
                        return;
                }

                if (playing)
                {
                    timer.Start();
                }
            }
        }

        private void TimerTick(object sender, EventArgs e)
        {
            uint ms = 0;
            bool playing = false;
            bool paused = false;

            if (channel != null)
            {
                RESULT result = channel.getPosition(out ms, TIMEUNIT.MS);
                if (result != RESULT.OK && result != RESULT.ERR_INVALID_HANDLE)
                {
                    ERRCHECK(result);
                }

                result = channel.isPlaying(out playing);
                if (result != RESULT.OK && result != RESULT.ERR_INVALID_HANDLE)
                {
                    ERRCHECK(result);
                }

                result = channel.getPaused(out paused);
                if (result != RESULT.OK && result != RESULT.ERR_INVALID_HANDLE)
                {
                    ERRCHECK(result);
                }
            }

            FMODtimerLabel.Text =
                ms / 1000 / 60 + ":" + ms / 1000 % 60 + "." + ms / 10 % 100 + " / " +
                FMODlenms / 1000 / 60 + ":" + FMODlenms / 1000 % 60 + "." + FMODlenms / 10 % 100;

            FMODprogressBar.Value = (int)(ms * 1000 / FMODlenms);
            FMODstatusLabel.Text = paused ? "Paused " : playing ? "Playing" : "Stopped";

            if (system != null)
                system.update();
        }
    }
}
