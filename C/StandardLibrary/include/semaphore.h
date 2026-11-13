#ifndef __SEMAPHORE_H
#define __SEMAPHORE_H

#include <limits.h>
#include <time.h>

typedef struct
{
    volatile int __value;
    volatile int __waiters;
    int __shared;
} sem_t;

#define SEM_FAILED ((sem_t*)0)

int sem_init(sem_t* semaphore, int shared, unsigned int value);
int sem_destroy(sem_t* semaphore);
int sem_wait(sem_t* semaphore);
int sem_trywait(sem_t* semaphore);
int sem_timedwait(sem_t* restrict semaphore, const struct timespec* restrict deadline);
int sem_post(sem_t* semaphore);
int sem_getvalue(sem_t* restrict semaphore, int* restrict value);

#endif
