#ifndef __SYS_SOUNDCARD_H
#define __SYS_SOUNDCARD_H

#include <linux/ioctl.h>

// The Open Sound System interface /dev/dsp speaks, as far as playback through it goes
#define SOUND_VERSION 0x040100

#define AFMT_QUERY 0x00000000
#define AFMT_MU_LAW 0x00000001
#define AFMT_A_LAW 0x00000002
#define AFMT_IMA_ADPCM 0x00000004
#define AFMT_U8 0x00000008
#define AFMT_S16_LE 0x00000010
#define AFMT_S16_BE 0x00000020
#define AFMT_S8 0x00000040
#define AFMT_U16_LE 0x00000080
#define AFMT_U16_BE 0x00000100
#define AFMT_S32_LE 0x00001000
#define AFMT_S32_BE 0x00002000
#define AFMT_FLOAT 0x00004000
#define AFMT_S16_NE AFMT_S16_LE
#define AFMT_S32_NE AFMT_S32_LE

typedef struct audio_buf_info
{
    int fragments;
    int fragstotal;
    int fragsize;
    int bytes;
} audio_buf_info;

#define SNDCTL_DSP_RESET _IO('P', 0)
#define SNDCTL_DSP_SYNC _IO('P', 1)
#define SNDCTL_DSP_SPEED _IOWR('P', 2, int)
#define SNDCTL_DSP_STEREO _IOWR('P', 3, int)
#define SNDCTL_DSP_GETBLKSIZE _IOWR('P', 4, int)
#define SNDCTL_DSP_SETFMT _IOWR('P', 5, int)
#define SNDCTL_DSP_SAMPLESIZE SNDCTL_DSP_SETFMT
#define SNDCTL_DSP_CHANNELS _IOWR('P', 6, int)
#define SNDCTL_DSP_POST _IO('P', 8)
#define SNDCTL_DSP_SETFRAGMENT _IOWR('P', 10, int)
#define SNDCTL_DSP_GETFMTS _IOR('P', 11, int)
#define SNDCTL_DSP_GETOSPACE _IOR('P', 12, audio_buf_info)
#define SNDCTL_DSP_NONBLOCK _IO('P', 14)
#define SNDCTL_DSP_GETCAPS _IOR('P', 15, int)
#define SNDCTL_DSP_GETODELAY _IOR('P', 23, int)
#define SOUND_PCM_WRITE_RATE SNDCTL_DSP_SPEED
#define SOUND_PCM_WRITE_CHANNELS SNDCTL_DSP_CHANNELS
#define OSS_GETVERSION _IOR('M', 118, int)

#endif
