#ifndef __SYS_IOCTL_H
#define __SYS_IOCTL_H

#if defined(__linux__)

#define TCGETS 0x5401
#define TCSETS 0x5402
#define TCSETSW 0x5403
#define TCSETSF 0x5404
#define TCSBRK 0x5409
#define TCXONC 0x540A
#define TCFLSH 0x540B
#define TIOCSCTTY 0x540E
#define TIOCGPGRP 0x540F
#define TIOCSPGRP 0x5410
#define TIOCOUTQ 0x5411
#define TIOCGWINSZ 0x5413
#define TIOCSWINSZ 0x5414
#define FIONREAD 0x541B
#define TIOCINQ FIONREAD
#define FIONBIO 0x5421
#define TIOCNOTTY 0x5422
#define TCSBRKP 0x5425
#define TIOCGSID 0x5429

struct winsize
{
    unsigned short ws_row;
    unsigned short ws_col;
    unsigned short ws_xpixel;
    unsigned short ws_ypixel;
};

int ioctl(int descriptor, unsigned long request, ...);

#endif

#endif
