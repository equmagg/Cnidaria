#ifndef __LINUX_INPUT_H
#define __LINUX_INPUT_H

#include <stdint.h>
#include <sys/time.h>
#include <linux/ioctl.h>
#include <linux/input-event-codes.h>

struct input_event
{
    struct timeval time;
    uint16_t type;
    uint16_t code;
    int32_t value;
};

#define EV_VERSION 0x010001

struct input_id
{
    uint16_t bustype;
    uint16_t vendor;
    uint16_t product;
    uint16_t version;
};

struct input_absinfo
{
    int32_t value;
    int32_t minimum;
    int32_t maximum;
    int32_t fuzz;
    int32_t flat;
    int32_t resolution;
};

#define EVIOCGVERSION _IOR('E', 0x01, int)
#define EVIOCGID _IOR('E', 0x02, struct input_id)
#define EVIOCGREP _IOR('E', 0x03, unsigned int[2])
#define EVIOCGNAME(len) _IOC(_IOC_READ, 'E', 0x06, len)
#define EVIOCGPHYS(len) _IOC(_IOC_READ, 'E', 0x07, len)
#define EVIOCGUNIQ(len) _IOC(_IOC_READ, 'E', 0x08, len)
#define EVIOCGPROP(len) _IOC(_IOC_READ, 'E', 0x09, len)
#define EVIOCGKEY(len) _IOC(_IOC_READ, 'E', 0x18, len)
#define EVIOCGLED(len) _IOC(_IOC_READ, 'E', 0x19, len)
#define EVIOCGSND(len) _IOC(_IOC_READ, 'E', 0x1a, len)
#define EVIOCGSW(len) _IOC(_IOC_READ, 'E', 0x1b, len)
#define EVIOCGBIT(ev, len) _IOC(_IOC_READ, 'E', 0x20 + (ev), len)
#define EVIOCGABS(abs) _IOR('E', 0x40 + (abs), struct input_absinfo)
#define EVIOCGRAB _IOW('E', 0x90, int)
#define EVIOCSCLOCKID _IOW('E', 0xa0, int)

#define BUS_USB 0x03
#define BUS_VIRTUAL 0x06

#endif
