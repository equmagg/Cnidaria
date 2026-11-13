#ifndef __PTHREAD_H
#define __PTHREAD_H

#include <limits.h>
#include <sched.h>
#include <stddef.h>
#include <sys/types.h>
#include <time.h>

#define PTHREAD_CREATE_JOINABLE 0
#define PTHREAD_CREATE_DETACHED 1

#define PTHREAD_MUTEX_NORMAL 0
#define PTHREAD_MUTEX_RECURSIVE 1
#define PTHREAD_MUTEX_ERRORCHECK 2
#define PTHREAD_MUTEX_DEFAULT PTHREAD_MUTEX_NORMAL

#define PTHREAD_PROCESS_PRIVATE 0
#define PTHREAD_PROCESS_SHARED 1

#define PTHREAD_BARRIER_SERIAL_THREAD (-1)

#define PTHREAD_MUTEX_INITIALIZER { 0, 0, 0, 0 }
#define PTHREAD_COND_INITIALIZER { 0, 0, 0, 0 }
#define PTHREAD_RWLOCK_INITIALIZER { 0, 0, 0 }
#define PTHREAD_ONCE_INIT 0

int pthread_create(pthread_t* restrict thread, const pthread_attr_t* restrict attr, void* (*start)(void*), void* restrict argument);
int pthread_join(pthread_t thread, void** result);
int pthread_detach(pthread_t thread);
void pthread_exit(void* result);
pthread_t pthread_self(void);
int pthread_equal(pthread_t left, pthread_t right);
int pthread_once(pthread_once_t* once, void (*routine)(void));
int pthread_atfork(void (*prepare)(void), void (*parent)(void), void (*child)(void));

int pthread_attr_init(pthread_attr_t* attr);
int pthread_attr_destroy(pthread_attr_t* attr);
int pthread_attr_getdetachstate(const pthread_attr_t* attr, int* state);
int pthread_attr_setdetachstate(pthread_attr_t* attr, int state);
int pthread_attr_getguardsize(const pthread_attr_t* restrict attr, size_t* restrict size);
int pthread_attr_setguardsize(pthread_attr_t* attr, size_t size);
int pthread_attr_getstacksize(const pthread_attr_t* restrict attr, size_t* restrict size);
int pthread_attr_setstacksize(pthread_attr_t* attr, size_t size);
int pthread_attr_getstack(const pthread_attr_t* restrict attr, void** restrict stack, size_t* restrict size);
int pthread_attr_setstack(pthread_attr_t* attr, void* stack, size_t size);

int pthread_mutex_init(pthread_mutex_t* restrict mutex, const pthread_mutexattr_t* restrict attr);
int pthread_mutex_destroy(pthread_mutex_t* mutex);
int pthread_mutex_lock(pthread_mutex_t* mutex);
int pthread_mutex_trylock(pthread_mutex_t* mutex);
int pthread_mutex_timedlock(pthread_mutex_t* restrict mutex, const struct timespec* restrict deadline);
int pthread_mutex_unlock(pthread_mutex_t* mutex);
int pthread_mutexattr_init(pthread_mutexattr_t* attr);
int pthread_mutexattr_destroy(pthread_mutexattr_t* attr);
int pthread_mutexattr_gettype(const pthread_mutexattr_t* restrict attr, int* restrict type);
int pthread_mutexattr_settype(pthread_mutexattr_t* attr, int type);
int pthread_mutexattr_getpshared(const pthread_mutexattr_t* restrict attr, int* restrict shared);
int pthread_mutexattr_setpshared(pthread_mutexattr_t* attr, int shared);

int pthread_cond_init(pthread_cond_t* restrict cond, const pthread_condattr_t* restrict attr);
int pthread_cond_destroy(pthread_cond_t* cond);
int pthread_cond_wait(pthread_cond_t* restrict cond, pthread_mutex_t* restrict mutex);
int pthread_cond_timedwait(pthread_cond_t* restrict cond, pthread_mutex_t* restrict mutex, const struct timespec* restrict deadline);
int pthread_cond_signal(pthread_cond_t* cond);
int pthread_cond_broadcast(pthread_cond_t* cond);
int pthread_condattr_init(pthread_condattr_t* attr);
int pthread_condattr_destroy(pthread_condattr_t* attr);
int pthread_condattr_getclock(const pthread_condattr_t* restrict attr, clockid_t* restrict clock);
int pthread_condattr_setclock(pthread_condattr_t* attr, clockid_t clock);
int pthread_condattr_getpshared(const pthread_condattr_t* restrict attr, int* restrict shared);
int pthread_condattr_setpshared(pthread_condattr_t* attr, int shared);

int pthread_rwlock_init(pthread_rwlock_t* restrict rwlock, const pthread_rwlockattr_t* restrict attr);
int pthread_rwlock_destroy(pthread_rwlock_t* rwlock);
int pthread_rwlock_rdlock(pthread_rwlock_t* rwlock);
int pthread_rwlock_tryrdlock(pthread_rwlock_t* rwlock);
int pthread_rwlock_timedrdlock(pthread_rwlock_t* restrict rwlock, const struct timespec* restrict deadline);
int pthread_rwlock_wrlock(pthread_rwlock_t* rwlock);
int pthread_rwlock_trywrlock(pthread_rwlock_t* rwlock);
int pthread_rwlock_timedwrlock(pthread_rwlock_t* restrict rwlock, const struct timespec* restrict deadline);
int pthread_rwlock_unlock(pthread_rwlock_t* rwlock);
int pthread_rwlockattr_init(pthread_rwlockattr_t* attr);
int pthread_rwlockattr_destroy(pthread_rwlockattr_t* attr);
int pthread_rwlockattr_getpshared(const pthread_rwlockattr_t* restrict attr, int* restrict shared);
int pthread_rwlockattr_setpshared(pthread_rwlockattr_t* attr, int shared);

int pthread_spin_init(pthread_spinlock_t* lock, int shared);
int pthread_spin_destroy(pthread_spinlock_t* lock);
int pthread_spin_lock(pthread_spinlock_t* lock);
int pthread_spin_trylock(pthread_spinlock_t* lock);
int pthread_spin_unlock(pthread_spinlock_t* lock);

int pthread_barrier_init(pthread_barrier_t* restrict barrier, const pthread_barrierattr_t* restrict attr, unsigned int count);
int pthread_barrier_destroy(pthread_barrier_t* barrier);
int pthread_barrier_wait(pthread_barrier_t* barrier);
int pthread_barrierattr_init(pthread_barrierattr_t* attr);
int pthread_barrierattr_destroy(pthread_barrierattr_t* attr);
int pthread_barrierattr_getpshared(const pthread_barrierattr_t* restrict attr, int* restrict shared);
int pthread_barrierattr_setpshared(pthread_barrierattr_t* attr, int shared);

int pthread_key_create(pthread_key_t* key, void (*destructor)(void*));
int pthread_key_delete(pthread_key_t key);
void* pthread_getspecific(pthread_key_t key);
int pthread_setspecific(pthread_key_t key, const void* value);

#endif
