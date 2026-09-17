typedef unsigned char u8;
typedef unsigned int u32;
typedef unsigned long u64;
typedef signed long s64;
typedef unsigned long usize;

#define AT_NULL 0ul
#define AT_HOST_BRIDGE 0x1000ul

#define HOST_MAGIC 0x4744524254534F48ul
#define HOST_REGISTER_MAGIC 0u
#define HOST_REGISTER_VERSION 1u
#define HOST_REGISTER_BUFFER_OFFSET 2u
#define HOST_REGISTER_BUFFER_SIZE 3u
#define HOST_REGISTER_FUNCTION 4u
#define HOST_REGISTER_ARGUMENT 5u
#define HOST_REGISTER_RESULT 9u
#define HOST_REGISTER_STATUS 10u
#define HOST_REGISTER_DOORBELL 11u

#define HOST_STATUS_NO_DEVICE 3l

static volatile u64* host_registers;
static u8* host_payload;
static u64 host_payload_size;

void __host_init(int argc, char** argv, char** envp)
{
    u64* auxv;
    u64 window;

    (void)argc;
    (void)argv;
    if (host_registers != (volatile u64*)0)
        return;

    auxv = (u64*)envp;
    while (*auxv != 0ul)
        auxv = auxv + 1;
    auxv = auxv + 1;

    window = 0ul;
    while (auxv[0] != AT_NULL)
    {
        if (auxv[0] == AT_HOST_BRIDGE)
            window = auxv[1];
        auxv = auxv + 2;
    }
    if (window == 0ul)
        return;

    host_registers = (volatile u64*)window;
    if (host_registers[HOST_REGISTER_MAGIC] != HOST_MAGIC)
    {
        host_registers = (volatile u64*)0;
        return;
    }
    host_payload = (u8*)(window + host_registers[HOST_REGISTER_BUFFER_OFFSET]);
    host_payload_size = host_registers[HOST_REGISTER_BUFFER_SIZE];
}

int host_available(void)
{
    return host_registers != (volatile u64*)0;
}

void* host_buffer(void)
{
    return (void*)host_payload;
}

u64 host_buffer_size(void)
{
    return host_payload_size;
}

s64 host_call(u64 function, u64 argument0, u64 argument1, u64 argument2, u64 argument3)
{
    if (host_registers == (volatile u64*)0)
        return HOST_STATUS_NO_DEVICE;

    host_registers[HOST_REGISTER_FUNCTION] = function;
    host_registers[HOST_REGISTER_ARGUMENT + 0u] = argument0;
    host_registers[HOST_REGISTER_ARGUMENT + 1u] = argument1;
    host_registers[HOST_REGISTER_ARGUMENT + 2u] = argument2;
    host_registers[HOST_REGISTER_ARGUMENT + 3u] = argument3;
    host_registers[HOST_REGISTER_DOORBELL] = 1ul;
    return (s64)host_registers[HOST_REGISTER_RESULT];
}

u64 host_status(void)
{
    if (host_registers == (volatile u64*)0)
        return (u64)HOST_STATUS_NO_DEVICE;
    return host_registers[HOST_REGISTER_STATUS];
}
