typedef unsigned char u8;
typedef unsigned short u16;
typedef unsigned int u32;
typedef unsigned long usize;
typedef signed long isize;

#define RH_SYS_WRITE 64ul
#define RH_SYS_EXIT 93ul
#define RH_SYS_MUNMAP 215ul
#define RH_SYS_MMAP 222ul
#define RH_SYS_MPROTECT 226ul
#define RH_PROT_NONE 0ul
#define RH_PROT_READ 1ul
#define RH_PROT_WRITE 2ul
#define RH_MAP_PRIVATE 2ul
#define RH_MAP_FIXED 16ul
#define RH_MAP_ANONYMOUS 32ul
#define RH_HEAP_RESERVE (64ul * 1024ul * 1024ul)
#define RH_HEAP_INITIAL_COMMIT (64ul * 1024ul)
#define RH_HEAP_COMMIT_GRANULARITY (64ul * 1024ul)
#define RH_PAGE_SIZE 4096ul
#define RH_HEAP_ALIGNMENT 16ul
#define RH_BLOCK_HEADER_SIZE (POINTER_SIZE * 4ul)
#define RH_BLOCK_FREE 0ul
#define RH_BLOCK_OBJECT 1ul
#define RH_BLOCK_MARK 1ul
#define RH_BLOCK_SCANNED 2ul
#define RH_ROOT_OBJECT 0ul
#define RH_ROOT_INTERIOR 1ul
#define RH_EETYPE_KIND_MASK 0x00030000u
#define RH_EETYPE_PARAMETERIZED_KIND 0x00020000u
#define RH_EETYPE_HAS_POINTERS 0x01000000u
#define RH_EETYPE_ELEMENT_TYPE_MASK 0x7c000000u
#define RH_EETYPE_ELEMENT_TYPE_SHIFT 26u
#define RH_EETYPE_ELEMENT_TYPE_CLASS 0x14u
#define RH_EETYPE_ELEMENT_TYPE_INTERFACE 0x15u
#define RH_EETYPE_ELEMENT_TYPE_SYSTEM_ARRAY 0x16u
#define RH_EETYPE_ELEMENT_TYPE_ARRAY 0x17u
#define RH_EETYPE_ELEMENT_TYPE_SZARRAY 0x18u
#define RH_EETYPE_ELEMENT_TYPE_BYREF 0x19u
#define RH_EETYPE_ELEMENT_TYPE_POINTER 0x1au
#define RH_EETYPE_HAS_COMPONENT_SIZE 0x80000000u
#define RH_EETYPE_COMPONENT_SIZE_MASK 0xffffu
#define RH_TYPE_FIXED 0ul
#define RH_TYPE_STRING 1ul
#define RH_TYPE_SZARRAY 2ul
#define RH_TYPE_MDARRAY 3ul
#define RH_TYPE_PARAMETERIZED 4ul
#define RH_MINIMUM_GC_OBJECT_SIZE (POINTER_SIZE * 3ul)
#define RH_EH_MAX_FRAMES 4096ul
#define RH_EH_MAX_CONTINUATIONS 256ul
#define RH_EH_MAX_CATCH_CONTEXTS 256ul
#define RH_EH_CATCH 1ul
#define RH_EH_CATCH_ALL 2ul
#define RH_EH_FINALLY 3ul
#define RH_EH_FAULT 4ul
#define RH_EH_CONTINUATION_LEAVE 1ul
#define RH_EH_CONTINUATION_THROW 2ul

typedef struct RhGcField
{
    usize offset;
    usize kind;
} RhGcField;

typedef struct RhMethodTable
{
    u32 flags;
    u32 base_size;
    const void* related_type;
    u16 vtable_slot_count;
    u16 interface_count;
    u32 hash_code;
} RhMethodTable;

typedef struct RhTypeInfo
{
    const RhMethodTable* type;
    usize gc_field_count;
    const RhGcField* gc_fields;
    usize component_gc_field_count;
    const RhGcField* component_gc_fields;
    usize runtime_kind;
} RhTypeInfo;

typedef struct RhRoot
{
    isize frame_offset;
    usize kind;
} RhRoot;

typedef struct RhSafePoint
{
    const void* return_address;
    isize saved_frame_pointer_offset;
    isize saved_return_address_offset;
    usize root_count;
    const RhRoot* roots;
} RhSafePoint;

typedef struct RhStaticRoot
{
    void* address;
    usize kind;
} RhStaticRoot;

typedef struct RhObject RhObject;

typedef struct RhEhClause
{
    usize kind;
    const void* try_start;
    const void* try_end;
    const void* handler_start;
    const void* handler_end;
    const RhMethodTable* catch_type;
    isize parent_index;
    isize source_try_start;
    isize source_try_end;
    isize source_handler_start;
    isize source_handler_end;
    isize source_handler_index;
} RhEhClause;

typedef struct RhEhMethodInfo
{
    usize clause_count;
    const RhEhClause* clauses;
} RhEhMethodInfo;

typedef struct RhEhFrame
{
    const RhEhMethodInfo* method;
    void* frame_pointer;
    const void* current_ip;
} RhEhFrame;

typedef union RhEhRegisterContext
{
    u8 data[512];
    usize alignment;
} RhEhRegisterContext;

typedef struct RhEhContinuation
{
    usize kind;
    const void* target;
    usize frame_index;
    const void* source_ip;
    isize clause_index;
    RhObject* exception;
} RhEhContinuation;

typedef struct RhCatchContext
{
    RhObject* exception;
    usize frame_index;
    const RhEhMethodInfo* method;
    isize clause_index;
} RhCatchContext;

typedef struct RhBlock
{
    usize size;
    usize kind;
    struct RhBlock* mark_next;
    usize flags;
} RhBlock;

struct RhObject
{
    const RhMethodTable* type;
};

static volatile usize rh_stack_base;
static const RhSafePoint* rh_safe_points;
static usize rh_safe_point_count;
static const RhTypeInfo* rh_type_infos;
static usize rh_type_info_count;
static const RhStaticRoot* rh_static_roots;
static usize rh_static_root_count;
static u8* rh_heap_base;
static u8* rh_heap_used;
static u8* rh_heap_committed;
static u8* rh_heap_limit;
static usize rh_allocation_debt;
static RhBlock* rh_mark_stack;
static RhBlock* rh_free_list;
static int rh_gc_running;
static RhObject* rh_delegate_temporary_root;
static RhObject* rh_active_exception;
static RhEhContinuation rh_eh_continuations[RH_EH_MAX_CONTINUATIONS];
static RhEhRegisterContext rh_eh_continuation_registers[RH_EH_MAX_CONTINUATIONS];
static usize rh_eh_continuation_count;
static RhCatchContext rh_catch_contexts[RH_EH_MAX_CATCH_CONTEXTS];
static usize rh_catch_context_count;

const RhSafePoint* RhpCurrentSafePoint;
void* RhpCurrentFramePointer;
usize RhpEhFrameCount;
RhEhFrame RhpEhFrames[RH_EH_MAX_FRAMES];
RhEhRegisterContext RhpEhRegisterContexts[RH_EH_MAX_FRAMES];
RhObject* RhpCurrentException;

void RhpFallbackFailFast(int code);
void RhpEhTransfer(void* frame_pointer, const void* target, const void* register_context);

static isize rh_syscall1(usize number, usize arg0)
{
    usize result = arg0;
    __asm__ volatile("ecall" : [result] "+{a0}"(result) : [number] "{a7}"(number) : "memory");
    return (isize)result;
}

static isize rh_syscall2(usize number, usize arg0, usize arg1)
{
    usize result = arg0;
    __asm__ volatile("ecall" : [result] "+{a0}"(result) : [arg1] "{a1}"(arg1), [number] "{a7}"(number) : "memory");
    return (isize)result;
}

static isize rh_syscall3(usize number, usize arg0, usize arg1, usize arg2)
{
    usize result = arg0;
    __asm__ volatile("ecall" : [result] "+{a0}"(result) : [arg1] "{a1}"(arg1), [arg2] "{a2}"(arg2), [number] "{a7}"(number) : "memory");
    return (isize)result;
}

static isize rh_syscall6(usize number, usize arg0, usize arg1, usize arg2, usize arg3, usize arg4, usize arg5)
{
    usize result = arg0;
    __asm__ volatile("ecall" : [result] "+{a0}"(result) : [arg1] "{a1}"(arg1), [arg2] "{a2}"(arg2), [arg3] "{a3}"(arg3), [arg4] "{a4}"(arg4), [arg5] "{a5}"(arg5), [number] "{a7}"(number) : "memory");
    return (isize)result;
}

static usize rh_align_up(usize value, usize alignment)
{
    return (value + alignment - 1ul) & ~(alignment - 1ul);
}

static usize rh_total_block_size(usize gc_size)
{
    usize maximum = (usize)-1;
    if (gc_size > maximum - RH_BLOCK_HEADER_SIZE - (RH_HEAP_ALIGNMENT - 1ul))
        return 0ul;
    return rh_align_up(RH_BLOCK_HEADER_SIZE + gc_size, RH_HEAP_ALIGNMENT);
}

static usize rh_component_size(const RhMethodTable* type)
{
    return (usize)(type->flags & RH_EETYPE_COMPONENT_SIZE_MASK);
}

static int rh_has_component_size(const RhMethodTable* type)
{
    return (type->flags & RH_EETYPE_HAS_COMPONENT_SIZE) != 0u;
}

static usize rh_method_table_kind(const RhMethodTable* type)
{
    return (usize)(type->flags & RH_EETYPE_KIND_MASK);
}

static usize rh_element_type(const RhMethodTable* type)
{
    return (usize)((type->flags & RH_EETYPE_ELEMENT_TYPE_MASK) >> RH_EETYPE_ELEMENT_TYPE_SHIFT);
}

static const RhMethodTable** rh_interface_map(const RhMethodTable* type)
{
    const void* map = *(const void**)((const u8*)type + 16ul + POINTER_SIZE);
    return (const RhMethodTable**)map;
}

static int rh_is_reference_type(const RhMethodTable* type)
{
    usize element_type = rh_element_type(type);
    return element_type >= RH_EETYPE_ELEMENT_TYPE_CLASS &&
        element_type <= RH_EETYPE_ELEMENT_TYPE_SZARRAY;
}

static int rh_is_assignable(const RhMethodTable* source, const RhMethodTable* target)
{
    while (1)
    {
        usize target_element_type;
        if (source == target)
            return 1;

        target_element_type = rh_element_type(target);
        if (target_element_type == RH_EETYPE_ELEMENT_TYPE_CLASS)
        {
            if (target->related_type == (const void*)0)
                return 1;
            if (rh_element_type(source) == RH_EETYPE_ELEMENT_TYPE_ARRAY ||
                rh_element_type(source) == RH_EETYPE_ELEMENT_TYPE_SZARRAY)
            {
                return 0;
            }
            source = (const RhMethodTable*)source->related_type;
            if (source == (const RhMethodTable*)0)
                return 0;
            continue;
        }

        if (target_element_type == RH_EETYPE_ELEMENT_TYPE_INTERFACE)
        {
            const RhMethodTable** interfaces = rh_interface_map(source);
            usize i = 0ul;
            if (interfaces == (const RhMethodTable**)0)
                return 0;
            while (i < (usize)source->interface_count)
            {
                if (interfaces[i] == target)
                    return 1;
                i = i + 1ul;
            }
            return 0;
        }

        if (target_element_type == RH_EETYPE_ELEMENT_TYPE_SYSTEM_ARRAY)
        {
            usize source_element_type = rh_element_type(source);
            return source_element_type == RH_EETYPE_ELEMENT_TYPE_ARRAY ||
                source_element_type == RH_EETYPE_ELEMENT_TYPE_SZARRAY;
        }

        if (target_element_type == RH_EETYPE_ELEMENT_TYPE_SZARRAY)
        {
            const RhMethodTable* source_element;
            const RhMethodTable* target_element;
            if (rh_element_type(source) != RH_EETYPE_ELEMENT_TYPE_SZARRAY)
                return 0;
            source_element = (const RhMethodTable*)source->related_type;
            target_element = (const RhMethodTable*)target->related_type;
            if (source_element == target_element)
                return 1;
            if (source_element == (const RhMethodTable*)0 ||
                target_element == (const RhMethodTable*)0 ||
                !rh_is_reference_type(source_element) ||
                !rh_is_reference_type(target_element))
            {
                return 0;
            }
            source = source_element;
            target = target_element;
            continue;
        }

        return 0;
    }
}

static int rh_eh_ip_in_range(const void* ip, const void* start, const void* end)
{
    usize value = (usize)ip;
    return value >= (usize)start && value < (usize)end;
}

static isize rh_eh_find_innermost_handler(const RhEhMethodInfo* method, const void* ip)
{
    isize best = -1;
    usize best_span = (usize)-1;
    usize i = 0ul;
    while (i < method->clause_count)
    {
        const RhEhClause* clause = &method->clauses[i];
        if (rh_eh_ip_in_range(ip, clause->handler_start, clause->handler_end))
        {
            usize span = (usize)clause->handler_end - (usize)clause->handler_start;
            if (span < best_span || (span == best_span && (isize)i > best))
            {
                best = (isize)i;
                best_span = span;
            }
        }
        i = i + 1ul;
    }
    return best;
}

static int rh_eh_source_range_contains(isize outer_start, isize outer_end, isize inner_start, isize inner_end)
{
    return inner_start >= outer_start && inner_end <= outer_end;
}

static int rh_eh_is_protected(const RhEhMethodInfo* method, const void* ip, usize clause_index)
{
    const RhEhClause* clause;
    isize current;
    if (clause_index >= method->clause_count)
        return 0;
    clause = &method->clauses[clause_index];
    if (rh_eh_ip_in_range(ip, clause->try_start, clause->try_end))
        return 1;
    current = rh_eh_find_innermost_handler(method, ip);
    if (current < 0)
        return 0;
    return rh_eh_source_range_contains(
        clause->source_try_start,
        clause->source_try_end,
        method->clauses[(usize)current].source_handler_start,
        method->clauses[(usize)current].source_handler_end);
}

static int rh_eh_handler_contains(const RhEhMethodInfo* method, isize clause_index, const void* ip)
{
    isize current;
    const RhEhClause* clause;
    if (clause_index < 0 || (usize)clause_index >= method->clause_count)
        return 0;
    current = rh_eh_find_innermost_handler(method, ip);
    if (current < 0)
        return 0;
    clause = &method->clauses[(usize)clause_index];
    return rh_eh_source_range_contains(
        clause->source_handler_start,
        clause->source_handler_end,
        method->clauses[(usize)current].source_handler_start,
        method->clauses[(usize)current].source_handler_end);
}

static int rh_eh_precedes(const RhEhClause* left, const RhEhClause* right)
{
    isize left_span = left->source_try_end - left->source_try_start;
    isize right_span = right->source_try_end - right->source_try_start;
    if (left_span != right_span)
        return left_span < right_span;
    if (left->source_try_start != right->source_try_start)
        return left->source_try_start > right->source_try_start;
    if (left->source_try_end != right->source_try_end)
        return left->source_try_end < right->source_try_end;
    if (left->source_handler_index != right->source_handler_index)
        return left->source_handler_index < right->source_handler_index;
    return left->source_handler_start < right->source_handler_start;
}

static int rh_eh_matches_catch(const RhEhClause* clause, const RhObject* exception)
{
    if (clause->kind == RH_EH_CATCH_ALL)
        return 1;
    if (clause->kind != RH_EH_CATCH || exception == (const RhObject*)0 || clause->catch_type == (const RhMethodTable*)0)
        return 0;
    return rh_is_assignable(exception->type, clause->catch_type);
}

static isize rh_eh_find_throw_handler(
    const RhEhFrame* frame,
    const RhObject* exception,
    const void* source_ip,
    isize after_clause_index)
{
    const RhEhMethodInfo* method = frame->method;
    const RhEhClause* after_clause = (const RhEhClause*)0;
    isize best = -1;
    usize i = 0ul;
    if (after_clause_index >= 0 && (usize)after_clause_index < method->clause_count)
        after_clause = &method->clauses[(usize)after_clause_index];
    while (i < method->clause_count)
    {
        const RhEhClause* clause = &method->clauses[i];
        if (rh_eh_is_protected(method, source_ip, i) &&
            (after_clause == (const RhEhClause*)0 || rh_eh_precedes(after_clause, clause)) &&
            (clause->kind == RH_EH_FINALLY || clause->kind == RH_EH_FAULT || rh_eh_matches_catch(clause, exception)) &&
            (best < 0 || rh_eh_precedes(clause, &method->clauses[(usize)best])))
        {
            best = (isize)i;
        }
        i = i + 1ul;
    }
    return best;
}

static isize rh_eh_find_leave_finally(
    const RhEhFrame* frame,
    const void* source_ip,
    const void* target,
    isize after_clause_index)
{
    const RhEhMethodInfo* method = frame->method;
    const RhEhClause* after_clause = (const RhEhClause*)0;
    isize best = -1;
    usize i = 0ul;
    if (after_clause_index >= 0 && (usize)after_clause_index < method->clause_count)
        after_clause = &method->clauses[(usize)after_clause_index];
    while (i < method->clause_count)
    {
        const RhEhClause* clause = &method->clauses[i];
        if (clause->kind == RH_EH_FINALLY &&
            rh_eh_is_protected(method, source_ip, i) &&
            !rh_eh_is_protected(method, target, i) &&
            (after_clause == (const RhEhClause*)0 || rh_eh_precedes(after_clause, clause)) &&
            (best < 0 || rh_eh_precedes(clause, &method->clauses[(usize)best])))
        {
            best = (isize)i;
        }
        i = i + 1ul;
    }
    return best;
}

static void rh_eh_copy_register_context(RhEhRegisterContext* destination, const RhEhRegisterContext* source)
{
    usize i = 0ul;
    while (i < 512ul)
    {
        destination->data[i] = source->data[i];
        i = i + 1ul;
    }
}

static void rh_eh_push_continuation(
    usize kind,
    const void* target,
    usize frame_index,
    const void* source_ip,
    isize clause_index,
    RhObject* exception)
{
    RhEhContinuation* continuation;
    if (rh_eh_continuation_count >= RH_EH_MAX_CONTINUATIONS)
        RhpFallbackFailFast(150);
    continuation = &rh_eh_continuations[rh_eh_continuation_count];
    continuation->kind = kind;
    continuation->target = target;
    continuation->frame_index = frame_index;
    continuation->source_ip = source_ip;
    continuation->clause_index = clause_index;
    continuation->exception = exception;
    rh_eh_copy_register_context(
        &rh_eh_continuation_registers[rh_eh_continuation_count],
        &RhpEhRegisterContexts[frame_index]);
    rh_eh_continuation_count = rh_eh_continuation_count + 1ul;
}

static void rh_eh_prune_continuations(usize frame_index, const void* target)
{
    usize source = 0ul;
    usize destination = 0ul;
    while (source < rh_eh_continuation_count)
    {
        RhEhContinuation continuation = rh_eh_continuations[source];
        int keep = continuation.frame_index < frame_index ||
            (continuation.frame_index == frame_index &&
                target != (const void*)0 &&
                rh_eh_handler_contains(
                    RhpEhFrames[frame_index].method,
                    continuation.clause_index,
                    target));
        if (keep)
        {
            if (destination != source)
            {
                rh_eh_continuations[destination] = continuation;
                rh_eh_copy_register_context(
                    &rh_eh_continuation_registers[destination],
                    &rh_eh_continuation_registers[source]);
            }
            destination = destination + 1ul;
        }
        source = source + 1ul;
    }
    rh_eh_continuation_count = destination;
}

static void rh_eh_refresh_current_exception(int preserve_active)
{
    if (rh_catch_context_count != 0ul)
        RhpCurrentException = rh_catch_contexts[rh_catch_context_count - 1ul].exception;
    else if (preserve_active)
        RhpCurrentException = rh_active_exception;
    else
        RhpCurrentException = (RhObject*)0;
}

static void rh_eh_prune_catches(usize frame_index, const void* target, int preserve_active)
{
    usize source = 0ul;
    usize destination = 0ul;
    while (source < rh_catch_context_count)
    {
        RhCatchContext context = rh_catch_contexts[source];
        int keep = context.frame_index < frame_index ||
            (context.frame_index == frame_index && rh_eh_handler_contains(context.method, context.clause_index, target));
        if (keep)
        {
            if (destination != source)
                rh_catch_contexts[destination] = context;
            destination = destination + 1ul;
        }
        source = source + 1ul;
    }
    rh_catch_context_count = destination;
    rh_eh_refresh_current_exception(preserve_active);
}

static void rh_eh_push_catch(RhObject* exception, usize frame_index, const RhEhMethodInfo* method, isize clause_index)
{
    RhCatchContext* context;
    if (rh_catch_context_count >= RH_EH_MAX_CATCH_CONTEXTS)
        RhpFallbackFailFast(150);
    context = &rh_catch_contexts[rh_catch_context_count];
    context->exception = exception;
    context->frame_index = frame_index;
    context->method = method;
    context->clause_index = clause_index;
    rh_catch_context_count = rh_catch_context_count + 1ul;
    RhpCurrentException = exception;
}

static void rh_eh_transfer(usize frame_index, const void* target)
{
    RhEhFrame* frame;
    if (frame_index >= RhpEhFrameCount || target == (const void*)0)
        RhpFallbackFailFast(150);
    rh_eh_prune_continuations(frame_index, target);
    RhpEhFrameCount = frame_index + 1ul;
    frame = &RhpEhFrames[frame_index];
    frame->current_ip = target;
    RhpEhTransfer(frame->frame_pointer, target, &RhpEhRegisterContexts[frame_index]);
    for (;;)
    {
    }
}

static void rh_eh_dispatch_from(
    RhObject* exception,
    usize frame_count,
    const void* source_ip,
    isize after_clause_index)
{
    while (frame_count != 0ul)
    {
        usize frame_index = frame_count - 1ul;
        RhEhFrame* frame = &RhpEhFrames[frame_index];
        const void* frame_source_ip = source_ip == (const void*)0 ? frame->current_ip : source_ip;
        isize clause_index = rh_eh_find_throw_handler(frame, exception, frame_source_ip, after_clause_index);
        if (clause_index >= 0)
        {
            const RhEhClause* clause = &frame->method->clauses[(usize)clause_index];
            rh_eh_prune_catches(frame_index, clause->handler_start, 1);
            if (clause->kind == RH_EH_FINALLY || clause->kind == RH_EH_FAULT)
            {
                rh_eh_push_continuation(
                    RH_EH_CONTINUATION_THROW,
                    (const void*)0,
                    frame_index,
                    frame_source_ip,
                    clause_index,
                    exception);
                rh_eh_transfer(frame_index, clause->handler_start);
            }
            rh_eh_push_catch(exception, frame_index, frame->method, clause_index);
            rh_eh_transfer(frame_index, clause->handler_start);
        }
        frame_count = frame_index;
        source_ip = (const void*)0;
        after_clause_index = -1;
    }
    RhpEhFrameCount = 0ul;
    rh_eh_continuation_count = 0ul;
    RhpCurrentException = exception;
    RhpFallbackFailFast(134);
}

static void rh_eh_dispatch(RhObject* exception)
{
    rh_eh_dispatch_from(exception, RhpEhFrameCount, (const void*)0, -1);
}

static void rh_eh_continue_leave(
    const void* target,
    const void* source_ip,
    isize after_clause_index)
{
    usize frame_index;
    RhEhFrame* frame;
    isize clause_index;
    if (RhpEhFrameCount == 0ul)
        RhpFallbackFailFast(150);
    frame_index = RhpEhFrameCount - 1ul;
    frame = &RhpEhFrames[frame_index];
    if (source_ip == (const void*)0)
        source_ip = frame->current_ip;
    clause_index = rh_eh_find_leave_finally(frame, source_ip, target, after_clause_index);
    if (clause_index >= 0)
    {
        const RhEhClause* clause = &frame->method->clauses[(usize)clause_index];
        rh_eh_prune_catches(frame_index, clause->handler_start, 0);
        rh_eh_push_continuation(
            RH_EH_CONTINUATION_LEAVE,
            target,
            frame_index,
            source_ip,
            clause_index,
            (RhObject*)0);
        rh_eh_transfer(frame_index, clause->handler_start);
    }
    rh_eh_prune_catches(frame_index, target, 0);
    rh_eh_transfer(frame_index, target);
}

void RhpThrowEx(RhObject* exception)
{
    if (exception == (RhObject*)0)
        RhpFallbackFailFast(134);
    rh_active_exception = exception;
    RhpCurrentException = exception;
    rh_eh_dispatch(exception);
}

void RhpRethrow(void)
{
    usize frame_index;
    isize i;
    RhObject* exception = (RhObject*)0;
    if (RhpEhFrameCount == 0ul)
        RhpFallbackFailFast(150);
    frame_index = RhpEhFrameCount - 1ul;
    i = (isize)rh_catch_context_count - 1;
    while (i >= 0)
    {
        RhCatchContext* context = &rh_catch_contexts[(usize)i];
        if (context->frame_index == frame_index &&
            rh_eh_handler_contains(context->method, context->clause_index, RhpEhFrames[frame_index].current_ip))
        {
            exception = context->exception;
            break;
        }
        i = i - 1;
    }
    if (exception == (RhObject*)0)
        RhpFallbackFailFast(150);
    rh_active_exception = exception;
    RhpCurrentException = exception;
    rh_eh_dispatch(exception);
}

void RhpLeave(const void* target, usize kind)
{
    if (kind == 0ul)
        RhpFallbackFailFast(150);
    rh_eh_continue_leave(target, (const void*)0, -1);
}

void RhpEndFinally(void)
{
    RhEhContinuation continuation;
    usize continuation_index;
    if (rh_eh_continuation_count == 0ul)
        RhpFallbackFailFast(150);
    rh_eh_continuation_count = rh_eh_continuation_count - 1ul;
    continuation_index = rh_eh_continuation_count;
    continuation = rh_eh_continuations[continuation_index];
    if (continuation.frame_index >= RhpEhFrameCount)
        RhpFallbackFailFast(150);
    rh_eh_copy_register_context(
        &RhpEhRegisterContexts[continuation.frame_index],
        &rh_eh_continuation_registers[continuation_index]);
    RhpEhFrames[continuation.frame_index].current_ip = continuation.source_ip;
    if (continuation.kind == RH_EH_CONTINUATION_THROW)
    {
        if (continuation.exception == (RhObject*)0)
            RhpFallbackFailFast(150);
        rh_active_exception = continuation.exception;
        RhpCurrentException = continuation.exception;
        rh_eh_dispatch_from(
            continuation.exception,
            continuation.frame_index + 1ul,
            continuation.source_ip,
            continuation.clause_index);
    }
    if (continuation.kind != RH_EH_CONTINUATION_LEAVE)
        RhpFallbackFailFast(150);
    rh_eh_continue_leave(
        continuation.target,
        continuation.source_ip,
        continuation.clause_index);
}

static const RhTypeInfo* rh_find_type_info(const RhMethodTable* type)
{
    usize i = 0ul;
    while (i < rh_type_info_count)
    {
        if (rh_type_infos[i].type == type)
            return &rh_type_infos[i];
        i = i + 1ul;
    }
    return (const RhTypeInfo*)0;
}

static const RhTypeInfo* rh_require_method_table(const RhMethodTable* type, int code)
{
    const RhTypeInfo* info;
    usize component_size;
    if (type == (const RhMethodTable*)0)
        RhpFallbackFailFast(code);
    info = rh_find_type_info(type);
    if (info == (const RhTypeInfo*)0 ||
        info->runtime_kind > RH_TYPE_PARAMETERIZED ||
        (info->gc_field_count != 0ul && info->gc_fields == (const RhGcField*)0) ||
        (info->component_gc_field_count != 0ul && info->component_gc_fields == (const RhGcField*)0) ||
        (((type->flags & RH_EETYPE_HAS_POINTERS) != 0u) !=
            (info->gc_field_count != 0ul || info->component_gc_field_count != 0ul)))
    {
        RhpFallbackFailFast(code);
    }

    component_size = rh_component_size(type);
    if (info->runtime_kind == RH_TYPE_FIXED)
    {
        if (rh_has_component_size(type) ||
            rh_method_table_kind(type) != 0ul ||
            type->base_size < RH_MINIMUM_GC_OBJECT_SIZE ||
            info->component_gc_field_count != 0ul)
        {
            RhpFallbackFailFast(code);
        }
        return info;
    }

    if (info->runtime_kind == RH_TYPE_PARAMETERIZED)
    {
        usize element_type = rh_element_type(type);
        if (rh_has_component_size(type) ||
            rh_method_table_kind(type) != RH_EETYPE_PARAMETERIZED_KIND ||
            type->related_type == (const void*)0 ||
            (element_type == RH_EETYPE_ELEMENT_TYPE_POINTER && type->base_size != 0u) ||
            (element_type == RH_EETYPE_ELEMENT_TYPE_BYREF && type->base_size != 1u) ||
            (element_type != RH_EETYPE_ELEMENT_TYPE_POINTER && element_type != RH_EETYPE_ELEMENT_TYPE_BYREF) ||
            info->gc_field_count != 0ul ||
            info->component_gc_field_count != 0ul)
        {
            RhpFallbackFailFast(code);
        }
        return info;
    }

    if (!rh_has_component_size(type) || component_size == 0ul)
        RhpFallbackFailFast(code);

    if (info->runtime_kind == RH_TYPE_STRING)
    {
        if (component_size != 2ul ||
            rh_method_table_kind(type) != 0ul ||
            rh_element_type(type) != RH_EETYPE_ELEMENT_TYPE_CLASS ||
            type->base_size < SYNC_BLOCK_SIZE + STRING_CHARS_OFFSET + 2ul ||
            info->component_gc_field_count != 0ul)
        {
            RhpFallbackFailFast(code);
        }
        return info;
    }

    if (rh_method_table_kind(type) != RH_EETYPE_PARAMETERIZED_KIND ||
        type->related_type == (const void*)0 ||
        type->base_size < SYNC_BLOCK_SIZE + ARRAY_DATA_OFFSET)
    {
        RhpFallbackFailFast(code);
    }

    if (info->runtime_kind == RH_TYPE_SZARRAY)
    {
        if (rh_element_type(type) != RH_EETYPE_ELEMENT_TYPE_SZARRAY ||
            type->base_size != SYNC_BLOCK_SIZE + ARRAY_DATA_OFFSET)
        {
            RhpFallbackFailFast(code);
        }
        return info;
    }

    if (rh_element_type(type) != RH_EETYPE_ELEMENT_TYPE_ARRAY ||
        type->base_size <= SYNC_BLOCK_SIZE + ARRAY_DATA_OFFSET ||
        ((type->base_size - (SYNC_BLOCK_SIZE + ARRAY_DATA_OFFSET)) & 7u) != 0u)
    {
        RhpFallbackFailFast(code);
    }
    return info;
}

static usize rh_variable_gc_size(const RhMethodTable* type, int length)
{
    usize maximum = (usize)-1;
    usize count;
    usize component_size;
    usize base_size;
    if (length < 0)
        return 0ul;
    count = (usize)length;
    component_size = rh_component_size(type);
    base_size = (usize)type->base_size;
    if (component_size == 0ul || count > (maximum - base_size) / component_size)
        return 0ul;
    return base_size + count * component_size;
}

static usize rh_minimum_block_size(void)
{
    usize object_block = rh_total_block_size(RH_MINIMUM_GC_OBJECT_SIZE);
    usize free_block = rh_align_up(RH_BLOCK_HEADER_SIZE + sizeof(RhBlock*), RH_HEAP_ALIGNMENT);
    return object_block > free_block ? object_block : free_block;
}

static void rh_zero(void* address, usize size)
{
    u8* p = (u8*)address;
    usize i = 0ul;
    while (i < size)
    {
        p[i] = 0u;
        i = i + 1ul;
    }
}

static void rh_memmove(void* destination, const void* source, usize size)
{
    u8* destination_bytes = (u8*)destination;
    const u8* source_bytes = (const u8*)source;
    if (destination_bytes == source_bytes || size == 0ul)
        return;
    if ((usize)destination_bytes < (usize)source_bytes ||
        (usize)destination_bytes >= (usize)source_bytes + size)
    {
        usize i = 0ul;
        while (i < size)
        {
            destination_bytes[i] = source_bytes[i];
            i = i + 1ul;
        }
        return;
    }
    while (size != 0ul)
    {
        size = size - 1ul;
        destination_bytes[size] = source_bytes[size];
    }
}

static void rh_write_all(const u8* data, usize length)
{
    usize offset = 0ul;
    while (offset < length)
    {
        isize result = rh_syscall3(RH_SYS_WRITE, 1ul, (usize)(data + offset), length - offset);
        if (result <= 0l)
            return;
        offset = offset + (usize)result;
    }
}

static usize rh_encode_utf8(u32 scalar, u8* buffer)
{
    if (scalar <= 0x7fu)
    {
        buffer[0] = (u8)scalar;
        return 1ul;
    }
    if (scalar <= 0x7ffu)
    {
        buffer[0] = (u8)(0xc0u | (scalar >> 6));
        buffer[1] = (u8)(0x80u | (scalar & 0x3fu));
        return 2ul;
    }
    if (scalar <= 0xffffu)
    {
        buffer[0] = (u8)(0xe0u | (scalar >> 12));
        buffer[1] = (u8)(0x80u | ((scalar >> 6) & 0x3fu));
        buffer[2] = (u8)(0x80u | (scalar & 0x3fu));
        return 3ul;
    }
    buffer[0] = (u8)(0xf0u | (scalar >> 18));
    buffer[1] = (u8)(0x80u | ((scalar >> 12) & 0x3fu));
    buffer[2] = (u8)(0x80u | ((scalar >> 6) & 0x3fu));
    buffer[3] = (u8)(0x80u | (scalar & 0x3fu));
    return 4ul;
}

void RhpFallbackFailFast(int code)
{
    rh_syscall1(RH_SYS_EXIT, (usize)code);
    for (;;)
    {
    }
}

static void* rh_os_reserve(usize size)
{
    isize result = rh_syscall6(
        RH_SYS_MMAP,
        0ul,
        size,
        RH_PROT_NONE,
        RH_MAP_PRIVATE | RH_MAP_ANONYMOUS,
        (usize)-1,
        0ul);
    if (result < 0l)
        return (void*)0;
    return (void*)(usize)result;
}

static int rh_os_commit(void* address, usize size)
{
    return rh_syscall3(
        RH_SYS_MPROTECT,
        (usize)address,
        size,
        RH_PROT_READ | RH_PROT_WRITE) == 0l;
}

static int rh_os_decommit(void* address, usize size)
{
    isize result;
    if (size == 0ul)
        return 1;
    result = rh_syscall6(
        RH_SYS_MMAP,
        (usize)address,
        size,
        RH_PROT_NONE,
        RH_MAP_PRIVATE | RH_MAP_ANONYMOUS | RH_MAP_FIXED,
        (usize)-1,
        0ul);
    return result == (isize)(usize)address;
}

static int rh_os_release(void* address, usize size)
{
    return rh_syscall2(RH_SYS_MUNMAP, (usize)address, size) == 0l;
}

static int rh_ensure_committed(u8* required)
{
    usize required_offset;
    usize committed_offset;
    usize target_offset;
    if (required <= rh_heap_committed)
        return 1;
    if (required > rh_heap_limit)
        return 0;
    required_offset = (usize)(required - rh_heap_base);
    committed_offset = (usize)(rh_heap_committed - rh_heap_base);
    target_offset = rh_align_up(required_offset, RH_HEAP_COMMIT_GRANULARITY);
    if (target_offset > RH_HEAP_RESERVE)
        target_offset = RH_HEAP_RESERVE;
    if (!rh_os_commit(rh_heap_base + committed_offset, target_offset - committed_offset))
        return 0;
    rh_heap_committed = rh_heap_base + target_offset;
    return 1;
}

static void rh_decommit_unused_tail(void)
{
    usize used_offset = (usize)(rh_heap_used - rh_heap_base);
    usize committed_offset = (usize)(rh_heap_committed - rh_heap_base);
    usize keep_offset = rh_align_up(used_offset, RH_PAGE_SIZE);
    if (keep_offset >= committed_offset)
        return;
    if (!rh_os_decommit(rh_heap_base + keep_offset, committed_offset - keep_offset))
        RhpFallbackFailFast(137);
    rh_heap_committed = rh_heap_base + keep_offset;
}

static void rh_initialize_heap(void)
{
    u8* base = (u8*)rh_os_reserve(RH_HEAP_RESERVE);
    if (base == (u8*)0 || (((usize)base) & (RH_HEAP_ALIGNMENT - 1ul)) != 0ul)
        RhpFallbackFailFast(137);
    if (!rh_os_commit(base, RH_HEAP_INITIAL_COMMIT))
    {
        rh_os_release(base, RH_HEAP_RESERVE);
        RhpFallbackFailFast(137);
    }
    rh_heap_base = base;
    rh_heap_used = base;
    rh_heap_committed = base + RH_HEAP_INITIAL_COMMIT;
    rh_heap_limit = base + RH_HEAP_RESERVE;
    rh_mark_stack = (RhBlock*)0;
    rh_free_list = (RhBlock*)0;
    rh_allocation_debt = 0ul;
}

void RhpInitialize(
    void* stack_base,
    const RhSafePoint* safe_points,
    usize safe_point_count,
    const RhTypeInfo* type_infos,
    usize type_info_count,
    const RhStaticRoot* static_roots,
    usize static_root_count)
{
    if (sizeof(usize) != POINTER_SIZE ||
        RH_PAGE_SIZE == 0ul ||
        (RH_PAGE_SIZE & (RH_PAGE_SIZE - 1ul)) != 0ul ||
        RH_HEAP_COMMIT_GRANULARITY < RH_PAGE_SIZE ||
        (RH_HEAP_COMMIT_GRANULARITY & (RH_HEAP_COMMIT_GRANULARITY - 1ul)) != 0ul ||
        RH_HEAP_INITIAL_COMMIT == 0ul ||
        RH_HEAP_INITIAL_COMMIT > RH_HEAP_RESERVE ||
        (RH_HEAP_INITIAL_COMMIT & (RH_PAGE_SIZE - 1ul)) != 0ul ||
        (RH_HEAP_RESERVE & (RH_PAGE_SIZE - 1ul)) != 0ul ||
        sizeof(int) != 4ul ||
        sizeof(u16) != 2ul ||
        SYNC_BLOCK_SIZE != POINTER_SIZE ||
        STRING_LENGTH_OFFSET != POINTER_SIZE ||
        STRING_CHARS_OFFSET != STRING_LENGTH_OFFSET + 4ul ||
        ARRAY_LENGTH_OFFSET != POINTER_SIZE ||
        ARRAY_DATA_OFFSET != POINTER_SIZE * 2ul ||
        sizeof(RhBlock) != RH_BLOCK_HEADER_SIZE ||
        sizeof(RhObject) != POINTER_SIZE ||
        sizeof(RhGcField) != POINTER_SIZE * 2ul ||
        sizeof(RhMethodTable) != 16ul + POINTER_SIZE ||
        sizeof(RhTypeInfo) != POINTER_SIZE * 6ul ||
        sizeof(RhRoot) != POINTER_SIZE * 2ul ||
        sizeof(RhSafePoint) != POINTER_SIZE * 5ul ||
        sizeof(RhStaticRoot) != POINTER_SIZE * 2ul ||
        sizeof(RhEhClause) != POINTER_SIZE * 12ul ||
        sizeof(RhEhMethodInfo) != POINTER_SIZE * 2ul ||
        sizeof(RhEhFrame) != POINTER_SIZE * 3ul ||
        sizeof(RhEhRegisterContext) != 512ul ||
        sizeof(RhEhContinuation) != POINTER_SIZE * 6ul ||
        sizeof(RhCatchContext) != POINTER_SIZE * 4ul ||
        stack_base == (void*)0 ||
        (safe_point_count != 0ul && safe_points == (const RhSafePoint*)0) ||
        (type_info_count != 0ul && type_infos == (const RhTypeInfo*)0) ||
        (static_root_count != 0ul && static_roots == (const RhStaticRoot*)0))
    {
        RhpFallbackFailFast(144);
    }
    rh_stack_base = (usize)stack_base;
    rh_safe_points = safe_points;
    rh_safe_point_count = safe_point_count;
    rh_type_infos = type_infos;
    rh_type_info_count = type_info_count;
    rh_static_roots = static_roots;
    rh_static_root_count = static_root_count;
    RhpCurrentSafePoint = (const RhSafePoint*)0;
    RhpCurrentFramePointer = (void*)0;
    RhpEhFrameCount = 0ul;
    RhpCurrentException = (RhObject*)0;
    rh_active_exception = (RhObject*)0;
    rh_eh_continuation_count = 0ul;
    rh_catch_context_count = 0ul;
    rh_initialize_heap();
}

static void rh_validate_block(u8* address, const RhBlock* block, int code)
{
    usize remaining;
    if (address < rh_heap_base || address >= rh_heap_used)
        RhpFallbackFailFast(code);
    remaining = (usize)(rh_heap_used - address);
    if ((((usize)address - (usize)rh_heap_base) & (RH_HEAP_ALIGNMENT - 1ul)) != 0ul ||
        block->size < rh_minimum_block_size() ||
        (block->size & (RH_HEAP_ALIGNMENT - 1ul)) != 0ul ||
        block->size > remaining ||
        (block->kind != RH_BLOCK_FREE && block->kind != RH_BLOCK_OBJECT))
    {
        RhpFallbackFailFast(code);
    }
}

static RhBlock* rh_free_next(RhBlock* block)
{
    return *(RhBlock**)((u8*)block + RH_BLOCK_HEADER_SIZE);
}

static void rh_set_free_next(RhBlock* block, RhBlock* next)
{
    *(RhBlock**)((u8*)block + RH_BLOCK_HEADER_SIZE) = next;
}

static RhObject* rh_object_from_block(RhBlock* block)
{
    return (RhObject*)((u8*)block + RH_BLOCK_HEADER_SIZE + SYNC_BLOCK_SIZE);
}

static usize rh_gc_object_size(const RhObject* object, const RhBlock* block, int code)
{
    const RhMethodTable* type = object->type;
    const RhTypeInfo* info;
    usize gc_size;
    info = rh_require_method_table(type, code);

    if (info->runtime_kind == RH_TYPE_FIXED)
    {
        gc_size = (usize)type->base_size;
    }
    else
    {
        int length;
        usize length_offset;
        if (info->runtime_kind == RH_TYPE_MDARRAY || info->runtime_kind == RH_TYPE_PARAMETERIZED)
            RhpFallbackFailFast(code);
        length_offset = info->runtime_kind == RH_TYPE_STRING
            ? STRING_LENGTH_OFFSET
            : ARRAY_LENGTH_OFFSET;
        if (block->size < RH_BLOCK_HEADER_SIZE + SYNC_BLOCK_SIZE + length_offset + 4ul)
            RhpFallbackFailFast(code);
        length = *(const int*)((const u8*)object + length_offset);
        gc_size = rh_variable_gc_size(type, length);
        if (gc_size == 0ul)
            RhpFallbackFailFast(code);
    }

    if (gc_size < SYNC_BLOCK_SIZE + POINTER_SIZE ||
        rh_total_block_size(gc_size) == 0ul ||
        rh_total_block_size(gc_size) > block->size)
    {
        RhpFallbackFailFast(code);
    }
    return gc_size;
}

static RhBlock* rh_block_for_exact_object(void* object)
{
    u8* p;
    u8* block_address;
    RhBlock* block;
    RhObject* value;
    if (object == (void*)0)
        return (RhBlock*)0;
    p = (u8*)object;
    if (p < rh_heap_base + RH_BLOCK_HEADER_SIZE + SYNC_BLOCK_SIZE || p >= rh_heap_used)
        return (RhBlock*)0;
    block_address = p - RH_BLOCK_HEADER_SIZE - SYNC_BLOCK_SIZE;
    if ((((usize)block_address - (usize)rh_heap_base) & (RH_HEAP_ALIGNMENT - 1ul)) != 0ul)
        return (RhBlock*)0;
    block = (RhBlock*)block_address;
    if (block->kind != RH_BLOCK_OBJECT || block->size > (usize)(rh_heap_used - block_address))
        return (RhBlock*)0;
    value = rh_object_from_block(block);
    if ((void*)value != object)
        return (RhBlock*)0;
    rh_gc_object_size(value, block, 138);
    return block;
}

static int rh_mark_object(void* object)
{
    RhBlock* block = rh_block_for_exact_object(object);
    if (block == (RhBlock*)0)
        return 0;
    if ((block->flags & RH_BLOCK_MARK) != 0ul)
        return 0;
    block->flags = RH_BLOCK_MARK;
    block->mark_next = rh_mark_stack;
    rh_mark_stack = block;
    return 1;
}

static int rh_mark_interior(void* interior)
{
    u8* target;
    u8* scan;
    if (interior == (void*)0)
        return 0;

    target = (u8*)interior;
    if (target < rh_heap_base + RH_BLOCK_HEADER_SIZE + SYNC_BLOCK_SIZE || target >= rh_heap_used)
        return 0;

    scan = rh_heap_base;
    while (scan < rh_heap_used)
    {
        RhBlock* block = (RhBlock*)scan;
        rh_validate_block(scan, block, 138);
        if (block->kind == RH_BLOCK_OBJECT)
        {
            RhObject* object = rh_object_from_block(block);
            usize gc_size = rh_gc_object_size(object, block, 138);
            u8* object_start = (u8*)object;
            u8* object_end = (u8*)block + RH_BLOCK_HEADER_SIZE + gc_size;
            if (target >= object_start && target < object_end)
                return rh_mark_object((void*)object_start);
            if (target < object_start)
                return 0;
        }
        scan = scan + block->size;
    }
    return 0;
}

static const RhSafePoint* rh_find_safe_point(const void* return_address)
{
    usize low = 0ul;
    usize high = rh_safe_point_count;
    usize key = (usize)return_address;
    while (low < high)
    {
        usize middle = low + (high - low) / 2ul;
        usize candidate = (usize)rh_safe_points[middle].return_address;
        if (candidate < key)
            low = middle + 1ul;
        else
            high = middle;
    }
    if (low < rh_safe_point_count && rh_safe_points[low].return_address == return_address)
        return &rh_safe_points[low];
    return (const RhSafePoint*)0;
}

static void rh_mark_frame_roots(const RhSafePoint* safe_point, usize frame_pointer)
{
    const RhSafePoint* current = safe_point;
    usize fp = frame_pointer;
    usize depth = 0ul;
    if (current == (const RhSafePoint*)0 || fp == 0ul || fp > rh_stack_base)
        RhpFallbackFailFast(144);

    while (depth < 4096ul)
    {
        usize i = 0ul;
        if (current->root_count != 0ul && current->roots == (const RhRoot*)0)
            RhpFallbackFailFast(144);
        while (i < current->root_count)
        {
            const RhRoot* root = &current->roots[i];
            void* value = *(void**)((u8*)fp + root->frame_offset);
            if (root->kind == RH_ROOT_INTERIOR)
                rh_mark_interior(value);
            else if (root->kind == RH_ROOT_OBJECT)
                rh_mark_object(value);
            else
                RhpFallbackFailFast(144);
            i = i + 1ul;
        }

        {
            usize caller_fp = *(usize*)((u8*)fp + current->saved_frame_pointer_offset);
            const void* caller_ra;
            if (caller_fp == 0ul)
                return;
            if (caller_fp <= fp || caller_fp > rh_stack_base)
                RhpFallbackFailFast(142);
            caller_ra = *(const void**)((u8*)fp + current->saved_return_address_offset);
            current = rh_find_safe_point(caller_ra);
            if (current == (const RhSafePoint*)0)
                RhpFallbackFailFast(143);
            fp = caller_fp;
        }
        depth = depth + 1ul;
    }
    RhpFallbackFailFast(144);
}

static void rh_mark_field_value(void* value, usize kind)
{
    if (kind == RH_ROOT_INTERIOR)
        rh_mark_interior(value);
    else if (kind == RH_ROOT_OBJECT)
        rh_mark_object(value);
    else
        RhpFallbackFailFast(139);
}

static void rh_mark_eh_register_context_roots(const RhEhRegisterContext* context)
{
    usize offset = 0ul;
    while (offset < 256ul)
    {
        void* value = *(void* const*)(context->data + offset);
        rh_mark_interior(value);
        offset = offset + 8ul;
    }
}

static void rh_mark_static_roots(void)
{
    usize i = 0ul;
    if (rh_static_root_count != 0ul && rh_static_roots == (const RhStaticRoot*)0)
        RhpFallbackFailFast(144);
    while (i < rh_static_root_count)
    {
        const RhStaticRoot* root = &rh_static_roots[i];
        if (root->address == (void*)0)
            RhpFallbackFailFast(144);
        rh_mark_field_value(*(void**)root->address, root->kind);
        i = i + 1ul;
    }
}

static void rh_drain_mark_stack(void)
{
    while (rh_mark_stack != (RhBlock*)0)
    {
        RhBlock* block = rh_mark_stack;
        RhObject* object;
        const RhMethodTable* type;
        const RhTypeInfo* info;
        usize gc_size;
        usize object_size;
        usize i = 0ul;
        rh_mark_stack = block->mark_next;
        block->mark_next = (RhBlock*)0;
        block->flags = RH_BLOCK_MARK | RH_BLOCK_SCANNED;
        if (block->kind != RH_BLOCK_OBJECT)
            RhpFallbackFailFast(139);

        object = rh_object_from_block(block);
        type = object->type;
        info = rh_require_method_table(type, 139);
        gc_size = rh_gc_object_size(object, block, 139);
        object_size = gc_size - SYNC_BLOCK_SIZE;

        while (i < info->gc_field_count)
        {
            const RhGcField* field = &info->gc_fields[i];
            void* value;
            if (object_size < POINTER_SIZE || field->offset > object_size - POINTER_SIZE)
                RhpFallbackFailFast(139);
            value = *(void**)((u8*)object + field->offset);
            rh_mark_field_value(value, field->kind);
            i = i + 1ul;
        }

        if (info->runtime_kind == RH_TYPE_SZARRAY && info->component_gc_field_count != 0ul)
        {
            int length = *(const int*)((const u8*)object + ARRAY_LENGTH_OFFSET);
            usize component_size = rh_component_size(type);
            usize element_index = 0ul;
            if (length < 0 || component_size < POINTER_SIZE)
                RhpFallbackFailFast(139);
            while (element_index < (usize)length)
            {
                u8* element = (u8*)object + ARRAY_DATA_OFFSET + element_index * component_size;
                usize field_index = 0ul;
                while (field_index < info->component_gc_field_count)
                {
                    const RhGcField* field = &info->component_gc_fields[field_index];
                    void* value;
                    if (field->offset > component_size - POINTER_SIZE)
                        RhpFallbackFailFast(139);
                    value = *(void**)(element + field->offset);
                    rh_mark_field_value(value, field->kind);
                    field_index = field_index + 1ul;
                }
                element_index = element_index + 1ul;
            }
        }
    }
}

static void rh_rebuild_free_list(void)
{
    u8* scan = rh_heap_base;
    RhBlock* tail = (RhBlock*)0;
    rh_free_list = (RhBlock*)0;
    while (scan < rh_heap_used)
    {
        RhBlock* block = (RhBlock*)scan;
        rh_validate_block(scan, block, 145);
        if (block->kind == RH_BLOCK_FREE)
        {
            block->mark_next = (RhBlock*)0;
            block->flags = 0ul;
            rh_set_free_next(block, (RhBlock*)0);
            if (tail == (RhBlock*)0)
                rh_free_list = block;
            else
                rh_set_free_next(tail, block);
            tail = block;
        }
        scan = scan + block->size;
    }
}

static void rh_sweep(void)
{
    u8* scan = rh_heap_base;
    u8* last_live_end = rh_heap_base;
    while (scan < rh_heap_used)
    {
        RhBlock* block = (RhBlock*)scan;
        rh_validate_block(scan, block, 145);
        if (block->kind == RH_BLOCK_OBJECT)
        {
            if ((block->flags & RH_BLOCK_MARK) == 0ul)
            {
                block->kind = RH_BLOCK_FREE;
                block->mark_next = (RhBlock*)0;
                block->flags = 0ul;
            }
            else
            {
                block->mark_next = (RhBlock*)0;
                block->flags = 0ul;
                last_live_end = scan + block->size;
            }
        }
        scan = scan + block->size;
    }

    scan = rh_heap_base;
    while (scan < rh_heap_used)
    {
        RhBlock* block = (RhBlock*)scan;
        rh_validate_block(scan, block, 145);
        if (block->kind == RH_BLOCK_FREE)
        {
            u8* next = scan + block->size;
            while (next < rh_heap_used)
            {
                RhBlock* next_block = (RhBlock*)next;
                rh_validate_block(next, next_block, 145);
                if (next_block->kind != RH_BLOCK_FREE)
                    break;
                block->size = block->size + next_block->size;
                next = scan + block->size;
            }
        }
        scan = scan + block->size;
    }

    rh_heap_used = last_live_end;
    rh_decommit_unused_tail();
    rh_rebuild_free_list();
    rh_allocation_debt = 0ul;
}

static void rh_collect(const RhSafePoint* safe_point, void* frame_pointer)
{
    if (rh_gc_running != 0)
        return;
    rh_gc_running = 1;
    rh_mark_stack = (RhBlock*)0;
    rh_mark_frame_roots(safe_point, (usize)frame_pointer);
    rh_mark_static_roots();
    rh_mark_object(rh_delegate_temporary_root);
    rh_mark_object(rh_active_exception);
    {
        usize i = 0ul;
        while (i < RhpEhFrameCount)
        {
            rh_mark_eh_register_context_roots(&RhpEhRegisterContexts[i]);
            i = i + 1ul;
        }
        i = 0ul;
        while (i < rh_eh_continuation_count)
        {
            rh_mark_object(rh_eh_continuations[i].exception);
            rh_mark_eh_register_context_roots(&rh_eh_continuation_registers[i]);
            i = i + 1ul;
        }
        i = 0ul;
        while (i < rh_catch_context_count)
        {
            rh_mark_object(rh_catch_contexts[i].exception);
            i = i + 1ul;
        }
    }
    rh_drain_mark_stack();
    rh_sweep();
    rh_gc_running = 0;
}

static void rh_collect_current(void)
{
    const RhSafePoint* safe_point = RhpCurrentSafePoint;
    void* frame_pointer = RhpCurrentFramePointer;
    if (safe_point == (const RhSafePoint*)0 || frame_pointer == (void*)0)
        RhpFallbackFailFast(144);
    rh_collect(safe_point, frame_pointer);
}

static void* rh_try_allocate_from_free_list(usize total)
{
    RhBlock* previous = (RhBlock*)0;
    RhBlock* block = rh_free_list;
    while (block != (RhBlock*)0)
    {
        RhBlock* next;
        u8* address = (u8*)block;
        rh_validate_block(address, block, 146);
        if (block->kind != RH_BLOCK_FREE)
            RhpFallbackFailFast(146);
        next = rh_free_next(block);
        if (block->size >= total)
        {
            usize remainder = block->size - total;
            if (remainder >= rh_minimum_block_size())
            {
                RhBlock* tail = (RhBlock*)(address + total);
                tail->size = remainder;
                tail->kind = RH_BLOCK_FREE;
                tail->mark_next = (RhBlock*)0;
                tail->flags = 0ul;
                rh_set_free_next(tail, next);
                if (previous == (RhBlock*)0)
                    rh_free_list = tail;
                else
                    rh_set_free_next(previous, tail);
                block->size = total;
            }
            else
            {
                if (previous == (RhBlock*)0)
                    rh_free_list = next;
                else
                    rh_set_free_next(previous, next);
            }
            block->kind = RH_BLOCK_OBJECT;
            block->mark_next = (RhBlock*)0;
            block->flags = 0ul;
            return (void*)(address + RH_BLOCK_HEADER_SIZE);
        }
        previous = block;
        block = next;
    }
    return (void*)0;
}

static void* rh_try_bump_allocate(usize total)
{
    RhBlock* block;
    u8* required;
    if (total > (usize)(rh_heap_limit - rh_heap_used))
        return (void*)0;
    required = rh_heap_used + total;
    if (!rh_ensure_committed(required))
        return (void*)0;
    block = (RhBlock*)rh_heap_used;
    block->size = total;
    block->kind = RH_BLOCK_OBJECT;
    block->mark_next = (RhBlock*)0;
    block->flags = 0ul;
    rh_heap_used = rh_heap_used + total;
    return (void*)((u8*)block + RH_BLOCK_HEADER_SIZE);
}

static void* rh_try_allocate(usize gc_size)
{
    usize total = rh_total_block_size(gc_size);
    void* storage;
    if (total == 0ul || total > RH_HEAP_RESERVE)
        return (void*)0;
    storage = rh_try_allocate_from_free_list(total);
    if (storage != (void*)0)
        return storage;
    return rh_try_bump_allocate(total);
}

static void* rh_allocate_object(const RhMethodTable* type, usize gc_size)
{
    void* storage;
    void* object;
    usize maximum = (usize)-1;

    if (rh_allocation_debt >= 262144ul)
        rh_collect_current();

    storage = rh_try_allocate(gc_size);
    if (storage == (void*)0)
    {
        rh_collect_current();
        storage = rh_try_allocate(gc_size);
        if (storage == (void*)0)
            RhpFallbackFailFast(141);
    }

    rh_zero(storage, gc_size);
    object = (void*)((u8*)storage + SYNC_BLOCK_SIZE);
    ((RhObject*)object)->type = type;
    if (rh_allocation_debt > maximum - gc_size)
        rh_allocation_debt = maximum;
    else
        rh_allocation_debt = rh_allocation_debt + gc_size;
    return object;
}

void* RhpNewFast(const RhMethodTable* type)
{
    const RhTypeInfo* info;
    usize gc_size;
    info = rh_require_method_table(type, 140);
    if (info->runtime_kind != RH_TYPE_FIXED)
        RhpFallbackFailFast(140);
    gc_size = (usize)type->base_size;
    if (rh_total_block_size(gc_size) == 0ul)
        RhpFallbackFailFast(140);
    return rh_allocate_object(type, gc_size);
}

void* RhpNewArray(const RhMethodTable* type, int length)
{
    const RhTypeInfo* info;
    usize gc_size;
    void* object;
    info = rh_require_method_table(type, 140);
    if (info->runtime_kind != RH_TYPE_SZARRAY && info->runtime_kind != RH_TYPE_STRING)
        RhpFallbackFailFast(140);
    gc_size = rh_variable_gc_size(type, length);
    if (gc_size == 0ul || rh_total_block_size(gc_size) == 0ul)
        RhpFallbackFailFast(140);

    object = rh_allocate_object(type, gc_size);
    if (info->runtime_kind == RH_TYPE_STRING)
        *(int*)((u8*)object + STRING_LENGTH_OFFSET) = length;
    else
        *(int*)((u8*)object + ARRAY_LENGTH_OFFSET) = length;
    return object;
}

static void* rh_delegate_read_slot(void* delegate_ref, usize offset)
{
    return *(void**)((u8*)delegate_ref + offset);
}

static void rh_delegate_write_slot(void* delegate_ref, usize offset, void* value)
{
    *(void**)((u8*)delegate_ref + offset) = value;
}

static usize rh_delegate_leaf_count(
    void* delegate_ref,
    const RhMethodTable* delegate_type,
    const RhMethodTable* array_type,
    usize invocation_list_offset,
    usize invocation_count_offset)
{
    void* list;
    usize count;
    int length;
    if (delegate_ref == (void*)0 || ((RhObject*)delegate_ref)->type != delegate_type)
        RhpFallbackFailFast(152);
    list = rh_delegate_read_slot(delegate_ref, invocation_list_offset);
    count = (usize)rh_delegate_read_slot(delegate_ref, invocation_count_offset);
    if (list == (void*)0)
    {
        if (count != 1ul)
            RhpFallbackFailFast(152);
        return 1ul;
    }
    if (count <= 1ul || ((RhObject*)list)->type != array_type || count > 2147483647ul)
        RhpFallbackFailFast(152);
    length = *(int*)((u8*)list + ARRAY_LENGTH_OFFSET);
    if (length < 0 || count >(usize)length)
        RhpFallbackFailFast(152);
    return count;
}

static void* rh_delegate_leaf_at(
    void* delegate_ref,
    usize index,
    const RhMethodTable* delegate_type,
    const RhMethodTable* array_type,
    usize invocation_list_offset,
    usize invocation_count_offset)
{
    void* list;
    usize count = rh_delegate_leaf_count(
        delegate_ref,
        delegate_type,
        array_type,
        invocation_list_offset,
        invocation_count_offset);
    if (index >= count)
        RhpFallbackFailFast(152);
    list = rh_delegate_read_slot(delegate_ref, invocation_list_offset);
    if (list == (void*)0 || count == 1ul)
        return delegate_ref;
    delegate_ref = *(void**)((u8*)list + ARRAY_DATA_OFFSET + index * POINTER_SIZE);
    if (delegate_ref == (void*)0 || ((RhObject*)delegate_ref)->type != delegate_type)
        RhpFallbackFailFast(152);
    return delegate_ref;
}

static int rh_delegate_same_leaf(
    void* left,
    void* right,
    const RhMethodTable* delegate_type,
    usize target_offset,
    usize method_ptr_offset)
{
    if (left == right)
        return 1;
    if (left == (void*)0 || right == (void*)0)
        return 0;
    if (((RhObject*)left)->type != delegate_type || ((RhObject*)right)->type != delegate_type)
        return 0;
    return rh_delegate_read_slot(left, target_offset) == rh_delegate_read_slot(right, target_offset) &&
        rh_delegate_read_slot(left, method_ptr_offset) == rh_delegate_read_slot(right, method_ptr_offset);
}

static void* rh_delegate_allocate_multicast(
    const RhMethodTable* delegate_type,
    const RhMethodTable* array_type,
    usize count,
    usize target_offset,
    usize method_ptr_offset,
    usize invocation_list_offset,
    usize invocation_count_offset)
{
    void* result;
    void* list;
    if (count < 2ul || count > 2147483647ul)
        RhpFallbackFailFast(152);
    result = RhpNewFast(delegate_type);
    rh_delegate_temporary_root = (RhObject*)result;
    list = RhpNewArray(array_type, (int)count);
    rh_delegate_write_slot(result, target_offset, (void*)0);
    rh_delegate_write_slot(result, method_ptr_offset, (void*)0);
    rh_delegate_write_slot(result, invocation_list_offset, list);
    rh_delegate_write_slot(result, invocation_count_offset, (void*)count);
    return result;
}

void* RhpDelegateCombine(
    void* left,
    void* right,
    const RhMethodTable* delegate_type,
    const RhMethodTable* array_type,
    usize target_offset,
    usize method_ptr_offset,
    usize invocation_list_offset,
    usize invocation_count_offset)
{
    usize left_count;
    usize right_count;
    usize total;
    usize index;
    void* result;
    void* list;
    if (left == (void*)0)
        return right;
    if (right == (void*)0)
        return left;
    if (((RhObject*)left)->type != delegate_type || ((RhObject*)right)->type != delegate_type)
        RhpFallbackFailFast(152);
    left_count = rh_delegate_leaf_count(left, delegate_type, array_type, invocation_list_offset, invocation_count_offset);
    right_count = rh_delegate_leaf_count(right, delegate_type, array_type, invocation_list_offset, invocation_count_offset);
    if (left_count > 2147483647ul - right_count)
        RhpFallbackFailFast(152);
    total = left_count + right_count;
    result = rh_delegate_allocate_multicast(
        delegate_type,
        array_type,
        total,
        target_offset,
        method_ptr_offset,
        invocation_list_offset,
        invocation_count_offset);
    list = rh_delegate_read_slot(result, invocation_list_offset);
    index = 0ul;
    while (index < left_count)
    {
        *(void**)((u8*)list + ARRAY_DATA_OFFSET + index * POINTER_SIZE) = rh_delegate_leaf_at(
            left,
            index,
            delegate_type,
            array_type,
            invocation_list_offset,
            invocation_count_offset);
        index = index + 1ul;
    }
    index = 0ul;
    while (index < right_count)
    {
        *(void**)((u8*)list + ARRAY_DATA_OFFSET + (left_count + index) * POINTER_SIZE) = rh_delegate_leaf_at(
            right,
            index,
            delegate_type,
            array_type,
            invocation_list_offset,
            invocation_count_offset);
        index = index + 1ul;
    }
    rh_delegate_temporary_root = (RhObject*)0;
    return result;
}

void* RhpDelegateRemove(
    void* source,
    void* value,
    const RhMethodTable* delegate_type,
    const RhMethodTable* array_type,
    usize target_offset,
    usize method_ptr_offset,
    usize invocation_list_offset,
    usize invocation_count_offset)
{
    usize source_count;
    usize value_count;
    usize start;
    usize compare_index;
    usize remove_at = (usize)-1;
    usize new_count;
    usize source_index;
    usize destination_index;
    void* result;
    void* list;
    if (source == (void*)0 || value == (void*)0)
        return source;
    if (((RhObject*)source)->type != delegate_type || ((RhObject*)value)->type != delegate_type)
        return source;
    source_count = rh_delegate_leaf_count(source, delegate_type, array_type, invocation_list_offset, invocation_count_offset);
    value_count = rh_delegate_leaf_count(value, delegate_type, array_type, invocation_list_offset, invocation_count_offset);
    if (value_count == 0ul || source_count < value_count)
        return source;
    start = source_count - value_count;
    for (;;)
    {
        compare_index = 0ul;
        while (compare_index < value_count && rh_delegate_same_leaf(
            rh_delegate_leaf_at(source, start + compare_index, delegate_type, array_type, invocation_list_offset, invocation_count_offset),
            rh_delegate_leaf_at(value, compare_index, delegate_type, array_type, invocation_list_offset, invocation_count_offset),
            delegate_type,
            target_offset,
            method_ptr_offset))
        {
            compare_index = compare_index + 1ul;
        }
        if (compare_index == value_count)
        {
            remove_at = start;
            break;
        }
        if (start == 0ul)
            break;
        start = start - 1ul;
    }
    if (remove_at == (usize)-1)
        return source;
    new_count = source_count - value_count;
    if (new_count == 0ul)
        return (void*)0;
    if (new_count == 1ul)
    {
        source_index = remove_at == 0ul ? value_count : 0ul;
        return rh_delegate_leaf_at(source, source_index, delegate_type, array_type, invocation_list_offset, invocation_count_offset);
    }
    result = rh_delegate_allocate_multicast(
        delegate_type,
        array_type,
        new_count,
        target_offset,
        method_ptr_offset,
        invocation_list_offset,
        invocation_count_offset);
    list = rh_delegate_read_slot(result, invocation_list_offset);
    source_index = 0ul;
    destination_index = 0ul;
    while (source_index < source_count)
    {
        if (source_index == remove_at)
        {
            source_index = source_index + value_count;
        }
        else
        {
            *(void**)((u8*)list + ARRAY_DATA_OFFSET + destination_index * POINTER_SIZE) = rh_delegate_leaf_at(
                source,
                source_index,
                delegate_type,
                array_type,
                invocation_list_offset,
                invocation_count_offset);
            source_index = source_index + 1ul;
            destination_index = destination_index + 1ul;
        }
    }
    rh_delegate_temporary_root = (RhObject*)0;
    return result;
}

static void rh_copy_utf16(u16* destination, const u16* source, int length)
{
    int i = 0;
    while (i < length)
    {
        destination[i] = source[i];
        i = i + 1;
    }
}

void* RhpNewStringFromChar(
    const RhMethodTable* type,
    u16 value,
    int length)
{
    void* object = RhpNewArray(type, length);
    u16* destination = (u16*)((u8*)object + STRING_CHARS_OFFSET);
    int i = 0;
    while (i < length)
    {
        destination[i] = value;
        i = i + 1;
    }
    return object;
}

void* RhpNewStringFromUtf16(
    const RhMethodTable* type,
    const u16* source)
{
    int length = 0;
    void* object;
    if (source == (const u16*)0)
        RhpFallbackFailFast(147);
    while (source[length] != 0u)
    {
        if (length == 0x7fffffff)
            RhpFallbackFailFast(140);
        length = length + 1;
    }
    object = RhpNewArray(type, length);
    rh_copy_utf16((u16*)((u8*)object + STRING_CHARS_OFFSET), source, length);
    return object;
}

static const RhMethodTable* rh_require_array(const void* array)
{
    const RhObject* object;
    const RhMethodTable* type;
    if (array == (const void*)0)
        RhpFallbackFailFast(147);
    object = (const RhObject*)array;
    type = object->type;
    if (rh_require_method_table(type, 147)->runtime_kind != RH_TYPE_SZARRAY ||
        *(const int*)((const u8*)array + ARRAY_LENGTH_OFFSET) < 0)
    {
        RhpFallbackFailFast(147);
    }
    return type;
}

static int rh_array_length(const void* array)
{
    rh_require_array(array);
    return *(const int*)((const u8*)array + ARRAY_LENGTH_OFFSET);
}

int RhpArrayGetLength(const void* array)
{
    return rh_array_length(array);
}

void RhpArrayClear(void* array, int index, int length)
{
    const RhMethodTable* type = rh_require_array(array);
    int array_length = rh_array_length(array);
    usize component_size = rh_component_size(type);
    usize byte_offset;
    usize byte_count;
    u8* destination;
    if (index < 0 || length < 0 || index > array_length || length > array_length - index)
        RhpFallbackFailFast(148);
    byte_offset = (usize)index * component_size;
    byte_count = (usize)length * component_size;
    destination = (u8*)array + ARRAY_DATA_OFFSET + byte_offset;
#ifdef __riscv_vector
    if (byte_count >= 16ul)
    {
        __asm__ volatile(
            "vsetvli a2, zero, e8, m8, ta, ma\n"
            "vxor.vv v8, v8, v8\n"
            ".Lrhp_array_clear_loop_%=:\n"
            "vsetvli a2, %[count], e8, m8, ta, ma\n"
            "vse8.v v8, (%[destination])\n"
            "add %[destination], %[destination], a2\n"
            "sub %[count], %[count], a2\n"
            "bne %[count], zero, .Lrhp_array_clear_loop_%="
            :
        : [destination] "{a0}"(destination), [count] "{a1}"(byte_count)
            : "memory");
        return;
    }
#endif
    rh_zero(destination, byte_count);
}

int RhpArrayCopy(
    const void* source_array,
    int source_index,
    void* destination_array,
    int destination_index,
    int length)
{
    const RhMethodTable* source_type = rh_require_array(source_array);
    const RhMethodTable* destination_type = rh_require_array(destination_array);
    int source_length = rh_array_length(source_array);
    int destination_length = rh_array_length(destination_array);
    const RhMethodTable* source_element;
    const RhMethodTable* destination_element;
    usize source_component_size;
    usize destination_component_size;
    usize byte_count;
    const u8* source;
    u8* destination;

    if (source_index < 0 || destination_index < 0 || length < 0 ||
        source_index > source_length || destination_index > destination_length ||
        length > source_length - source_index || length > destination_length - destination_index)
    {
        RhpFallbackFailFast(148);
    }

    source_element = (const RhMethodTable*)source_type->related_type;
    destination_element = (const RhMethodTable*)destination_type->related_type;
    source_component_size = rh_component_size(source_type);
    destination_component_size = rh_component_size(destination_type);

    if (source_element == destination_element)
    {
        if (source_component_size != destination_component_size)
            RhpFallbackFailFast(149);
        byte_count = (usize)length * source_component_size;
        source = (const u8*)source_array + ARRAY_DATA_OFFSET + (usize)source_index * source_component_size;
        destination = (u8*)destination_array + ARRAY_DATA_OFFSET + (usize)destination_index * destination_component_size;
    }
    else
    {
        if (source_component_size != POINTER_SIZE || destination_component_size != POINTER_SIZE ||
            source_element == (const RhMethodTable*)0 || destination_element == (const RhMethodTable*)0 ||
            !rh_is_reference_type(source_element) || !rh_is_reference_type(destination_element))
        {
            return 0;
        }

        source = (const u8*)source_array + ARRAY_DATA_OFFSET + (usize)source_index * POINTER_SIZE;
        destination = (u8*)destination_array + ARRAY_DATA_OFFSET + (usize)destination_index * POINTER_SIZE;
        if (!rh_is_assignable(source_element, destination_element))
        {
            usize i = 0ul;
            while (i < (usize)length)
            {
                const RhObject* value = *(const RhObject**)(source + i * POINTER_SIZE);
                if (value != (const RhObject*)0 && !rh_is_assignable(value->type, destination_element))
                    return 0;
                i = i + 1ul;
            }
        }
        byte_count = (usize)length * POINTER_SIZE;
    }

#ifdef __riscv_vector
    if (byte_count >= 16ul)
    {
        if (destination == source)
            return 1;
        if ((usize)destination < (usize)source ||
            (usize)destination >= (usize)source + byte_count)
        {
            __asm__ volatile(
                ".Lrhp_array_copy_forward_loop_%=:\n"
                "vsetvli a3, %[count], e8, m8, ta, ma\n"
                "vle8.v v8, (%[source])\n"
                "vse8.v v8, (%[destination])\n"
                "add %[source], %[source], a3\n"
                "add %[destination], %[destination], a3\n"
                "sub %[count], %[count], a3\n"
                "bne %[count], zero, .Lrhp_array_copy_forward_loop_%="
                :
            : [destination] "{a0}"(destination), [source] "{a1}"(source), [count] "{a2}"(byte_count)
                : "memory");
            return 1;
        }

        source = source + byte_count;
        destination = destination + byte_count;
        __asm__ volatile(
            ".Lrhp_array_copy_backward_loop_%=:\n"
            "vsetvli a3, %[count], e8, m8, ta, ma\n"
            "sub %[source], %[source], a3\n"
            "sub %[destination], %[destination], a3\n"
            "vle8.v v8, (%[source])\n"
            "vse8.v v8, (%[destination])\n"
            "sub %[count], %[count], a3\n"
            "bne %[count], zero, .Lrhp_array_copy_backward_loop_%="
            :
        : [destination] "{a0}"(destination), [source] "{a1}"(source), [count] "{a2}"(byte_count)
            : "memory");
        return 1;
    }
#endif

    rh_memmove(destination, source, byte_count);
    return 1;
}

void* RhpNewStringFromCharArray(
    const RhMethodTable* type,
    const void* array)
{
    const RhMethodTable* array_type = rh_require_array(array);
    int length;
    const u16* source;
    void* object;
    if (rh_component_size(array_type) != 2ul)
        RhpFallbackFailFast(147);
    length = rh_array_length(array);
    source = (const u16*)((const u8*)array + ARRAY_DATA_OFFSET);
    object = RhpNewArray(type, length);
    rh_copy_utf16((u16*)((u8*)object + STRING_CHARS_OFFSET), source, length);
    return object;
}

void* RhpNewStringFromCharArrayRange(
    const RhMethodTable* type,
    const void* array,
    int start_index,
    int length)
{
    const RhMethodTable* array_type = rh_require_array(array);
    int array_length;
    const u16* source;
    void* object;
    if (rh_component_size(array_type) != 2ul)
        RhpFallbackFailFast(147);
    array_length = rh_array_length(array);
    if (start_index < 0 || length < 0 || start_index > array_length || length > array_length - start_index)
        RhpFallbackFailFast(147);
    source = (const u16*)((const u8*)array + ARRAY_DATA_OFFSET) + start_index;
    object = RhpNewArray(type, length);
    rh_copy_utf16((u16*)((u8*)object + STRING_CHARS_OFFSET), source, length);
    return object;
}

void RhpGcPoll(void)
{
    if (rh_allocation_debt >= 262144ul)
        rh_collect_current();
}

static const RhMethodTable* rh_require_string(const void* value)
{
    const RhObject* object;
    const RhMethodTable* type;
    if (value == (const void*)0)
        RhpFallbackFailFast(147);
    object = (const RhObject*)value;
    type = object->type;
    if (rh_require_method_table(type, 147)->runtime_kind != RH_TYPE_STRING ||
        *(const int*)((const u8*)value + STRING_LENGTH_OFFSET) < 0)
    {
        RhpFallbackFailFast(147);
    }
    return type;
}

int RhpStringGetLength(const void* value)
{
    rh_require_string(value);
    return *(const int*)((const u8*)value + STRING_LENGTH_OFFSET);
}

u16* RhpStringGetData(void* value)
{
    rh_require_string(value);
    return (u16*)((u8*)value + STRING_CHARS_OFFSET);
}

void RhpConsoleWriteUtf16(const u16* text, int length)
{
    int index = 0;
    if (text == (const u16*)0 || length <= 0)
        return;

    while (index < length)
    {
        u32 scalar = (u32)text[index];
        u8 encoded[4];
        usize encoded_length;
        index = index + 1;

        if (scalar >= 0xd800u && scalar <= 0xdbffu)
        {
            if (index < length)
            {
                u32 low = (u32)text[index];
                if (low >= 0xdc00u && low <= 0xdfffu)
                {
                    scalar = 0x10000u + ((scalar - 0xd800u) << 10) + (low - 0xdc00u);
                    index = index + 1;
                }
                else
                {
                    scalar = 0xfffdu;
                }
            }
            else
            {
                scalar = 0xfffdu;
            }
        }
        else if (scalar >= 0xdc00u && scalar <= 0xdfffu)
        {
            scalar = 0xfffdu;
        }

        encoded_length = rh_encode_utf8(scalar, encoded);
        rh_write_all(encoded, encoded_length);
    }
}

void RhpConsoleWriteUtf16Z(const u16* text)
{
    int length = 0;
    if (text == (const u16*)0)
        return;
    while (text[length] != 0u)
        length = length + 1;
    RhpConsoleWriteUtf16(text, length);
}

void RhpConsoleWriteString(const void* value)
{
    const u8* object;
    int length;
    const u16* chars;
    if (value == (const void*)0)
        return;
    object = (const u8*)value;
    rh_require_string(value);
    length = *(const int*)(object + STRING_LENGTH_OFFSET);
    chars = (const u16*)(object + STRING_CHARS_OFFSET);
    RhpConsoleWriteUtf16(chars, length);
}
