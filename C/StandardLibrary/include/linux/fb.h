#ifndef __LINUX_FB_H
#define __LINUX_FB_H

#include <stdint.h>
#include <linux/ioctl.h>

#define FBIOGET_VSCREENINFO 0x4600
#define FBIOPUT_VSCREENINFO 0x4601
#define FBIOGET_FSCREENINFO 0x4602
#define FBIOGETCMAP 0x4604
#define FBIOPUTCMAP 0x4605
#define FBIOPAN_DISPLAY 0x4606
#define FBIOBLANK 0x4611
#define FBIOGET_VBLANK _IOR('F', 0x12, struct fb_vblank)
#define FBIO_WAITFORVSYNC _IOW('F', 0x20, uint32_t)

#define FB_TYPE_PACKED_PIXELS 0
#define FB_VISUAL_TRUECOLOR 2
#define FB_VISUAL_PSEUDOCOLOR 3
#define FB_VISUAL_DIRECTCOLOR 4
#define FB_ACCEL_NONE 0
#define FB_ACTIVATE_NOW 0
#define FB_ACTIVATE_NXTOPEN 1
#define FB_ACTIVATE_TEST 2
#define FB_ACTIVATE_MASK 15
#define FB_ACTIVATE_VBL 16
#define FB_ACTIVATE_ALL 64
#define FB_ACTIVATE_FORCE 128
#define FB_VMODE_NONINTERLACED 0
#define FB_VMODE_INTERLACED 1
#define FB_VMODE_DOUBLE 2
#define FB_VMODE_MASK 255
#define FB_VMODE_YWRAP 256
#define FB_ROTATE_UR 0
#define FB_ROTATE_CW 1
#define FB_ROTATE_UD 2
#define FB_ROTATE_CCW 3
#define FB_BLANK_UNBLANK 0
#define FB_BLANK_NORMAL 1
#define FB_BLANK_VSYNC_SUSPEND 2
#define FB_BLANK_HSYNC_SUSPEND 3
#define FB_BLANK_POWERDOWN 4

struct fb_fix_screeninfo {
    char id[16];
    unsigned long smem_start;
    uint32_t smem_len;
    uint32_t type;
    uint32_t type_aux;
    uint32_t visual;
    uint16_t xpanstep;
    uint16_t ypanstep;
    uint16_t ywrapstep;
    uint32_t line_length;
    unsigned long mmio_start;
    uint32_t mmio_len;
    uint32_t accel;
    uint16_t capabilities;
    uint16_t reserved[2];
};

struct fb_bitfield {
    uint32_t offset;
    uint32_t length;
    uint32_t msb_right;
};

struct fb_var_screeninfo {
    uint32_t xres;
    uint32_t yres;
    uint32_t xres_virtual;
    uint32_t yres_virtual;
    uint32_t xoffset;
    uint32_t yoffset;
    uint32_t bits_per_pixel;
    uint32_t grayscale;
    struct fb_bitfield red;
    struct fb_bitfield green;
    struct fb_bitfield blue;
    struct fb_bitfield transp;
    uint32_t nonstd;
    uint32_t activate;
    uint32_t height;
    uint32_t width;
    uint32_t accel_flags;
    uint32_t pixclock;
    uint32_t left_margin;
    uint32_t right_margin;
    uint32_t upper_margin;
    uint32_t lower_margin;
    uint32_t hsync_len;
    uint32_t vsync_len;
    uint32_t sync;
    uint32_t vmode;
    uint32_t rotate;
    uint32_t colorspace;
    uint32_t reserved[4];
};

struct fb_cmap {
    uint32_t start;
    uint32_t len;
    uint16_t* red;
    uint16_t* green;
    uint16_t* blue;
    uint16_t* transp;
};

struct fb_vblank {
    uint32_t flags;
    uint32_t count;
    uint32_t vcount;
    uint32_t hcount;
    uint32_t reserved[4];
};

#endif
