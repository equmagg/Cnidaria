#include <errno.h>
#include <string.h>
#include <sys/ioctl.h>
#include <termios.h>
#include <unistd.h>

#if defined(__linux__)

// The kernel keeps nineteen control characters and no separate speeds; the rest of the structure is the library's
#define __TERMIOS_KERNEL_NCCS 19
#define __TERMIOS_KERNEL_SIZE 36

int tcgetattr(int descriptor, struct termios* attributes)
{
    unsigned char raw[__TERMIOS_KERNEL_SIZE];

    if (ioctl(descriptor, TCGETS, raw) < 0)
        return -1;
    memset(attributes, 0, sizeof(*attributes));
    memcpy(&attributes->c_iflag, raw, 4);
    memcpy(&attributes->c_oflag, raw + 4, 4);
    memcpy(&attributes->c_cflag, raw + 8, 4);
    memcpy(&attributes->c_lflag, raw + 12, 4);
    attributes->c_line = raw[16];
    memcpy(attributes->c_cc, raw + 17, __TERMIOS_KERNEL_NCCS);
    attributes->__c_ispeed = attributes->c_cflag & CBAUD;
    attributes->__c_ospeed = attributes->c_cflag & CBAUD;
    return 0;
}

int tcsetattr(int descriptor, int when, const struct termios* attributes)
{
    unsigned char raw[__TERMIOS_KERNEL_SIZE];

    if (when < TCSANOW || when > TCSAFLUSH)
    {
        errno = EINVAL;
        return -1;
    }
    memcpy(raw, &attributes->c_iflag, 4);
    memcpy(raw + 4, &attributes->c_oflag, 4);
    memcpy(raw + 8, &attributes->c_cflag, 4);
    memcpy(raw + 12, &attributes->c_lflag, 4);
    raw[16] = attributes->c_line;
    memcpy(raw + 17, attributes->c_cc, __TERMIOS_KERNEL_NCCS);
    return ioctl(descriptor, TCSETS + when, raw);
}

void cfmakeraw(struct termios* attributes)
{
    attributes->c_iflag = attributes->c_iflag & ~(tcflag_t)(IGNBRK | BRKINT | PARMRK | ISTRIP | INLCR | IGNCR | ICRNL | IXON);
    attributes->c_oflag = attributes->c_oflag & ~(tcflag_t)OPOST;
    attributes->c_lflag = attributes->c_lflag & ~(tcflag_t)(ECHO | ECHONL | ICANON | ISIG | IEXTEN);
    attributes->c_cflag = (attributes->c_cflag & ~(tcflag_t)(CSIZE | PARENB)) | CS8;
    attributes->c_cc[VMIN] = 1;
    attributes->c_cc[VTIME] = 0;
}

speed_t cfgetispeed(const struct termios* attributes)
{
    return attributes->c_cflag & CBAUD;
}

speed_t cfgetospeed(const struct termios* attributes)
{
    return attributes->c_cflag & CBAUD;
}

int cfsetospeed(struct termios* attributes, speed_t speed)
{
    if ((speed & ~(speed_t)CBAUD) != 0)
    {
        errno = EINVAL;
        return -1;
    }
    attributes->c_cflag = (attributes->c_cflag & ~(tcflag_t)CBAUD) | speed;
    attributes->__c_ispeed = speed;
    attributes->__c_ospeed = speed;
    return 0;
}

int cfsetispeed(struct termios* attributes, speed_t speed)
{
    return speed == 0 ? 0 : cfsetospeed(attributes, speed);
}

int cfsetspeed(struct termios* attributes, speed_t speed)
{
    return cfsetospeed(attributes, speed);
}

int tcflush(int descriptor, int queue)
{
    return ioctl(descriptor, TCFLSH, (long)queue);
}

int tcdrain(int descriptor)
{
    return ioctl(descriptor, TCSBRK, 1l);
}

int tcflow(int descriptor, int action)
{
    return ioctl(descriptor, TCXONC, (long)action);
}

int tcsendbreak(int descriptor, int duration)
{
    (void)duration;
    return ioctl(descriptor, TCSBRK, 0l);
}

pid_t tcgetsid(int descriptor)
{
    int session;

    if (ioctl(descriptor, TIOCGSID, &session) < 0)
        return -1;
    return session;
}

pid_t tcgetpgrp(int descriptor)
{
    int group;

    if (ioctl(descriptor, TIOCGPGRP, &group) < 0)
        return -1;
    return group;
}

int tcsetpgrp(int descriptor, pid_t group)
{
    int value = group;
    return ioctl(descriptor, TIOCSPGRP, &value);
}

#endif
