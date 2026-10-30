#ifndef __LINUX_IOCTL_H
#define __LINUX_IOCTL_H

// A request number packs its direction, argument size, driver letter and number as asm-generic/ioctl.h does
#define _IOC_NONE 0u
#define _IOC_WRITE 1u
#define _IOC_READ 2u

#define _IOC(dir, type, nr, size) ((((unsigned long)(dir)) << 30) | (((unsigned long)(size)) << 16) | (((unsigned long)(type)) << 8) | ((unsigned long)(nr)))
#define _IO(type, nr) _IOC(_IOC_NONE, (type), (nr), 0)
#define _IOR(type, nr, argument) _IOC(_IOC_READ, (type), (nr), sizeof(argument))
#define _IOW(type, nr, argument) _IOC(_IOC_WRITE, (type), (nr), sizeof(argument))
#define _IOWR(type, nr, argument) _IOC(_IOC_READ | _IOC_WRITE, (type), (nr), sizeof(argument))

#define _IOC_DIR(request) (((request) >> 30) & 3u)
#define _IOC_TYPE(request) (((request) >> 8) & 0xffu)
#define _IOC_NR(request) ((request) & 0xffu)
#define _IOC_SIZE(request) (((request) >> 16) & 0x3fffu)

#endif
