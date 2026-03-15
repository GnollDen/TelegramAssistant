do $$
begin
    create extension if not exists vector;
exception
    when insufficient_privilege then
        if not exists (
            select 1
            from pg_extension
            where extname = 'vector'
        ) then
            raise;
        end if;
end $$;

drop index if exists idx_text_embeddings_vector_ivfflat;

do $$
begin
    if exists (
        select 1
        from information_schema.columns
        where table_schema = 'public'
          and table_name = 'text_embeddings'
          and column_name = 'vector'
          and udt_name <> 'vector'
    ) then
        alter table text_embeddings alter column vector drop not null;
        update text_embeddings
        set vector = array_fill(0::real, array[1536])
        where coalesce(array_length(vector, 1), 0) = 0;
        alter table text_embeddings alter column vector drop default;
        alter table text_embeddings alter column vector type vector using replace(replace(vector::text, '{', '['), '}', ']')::vector;
        alter table text_embeddings alter column vector set not null;
    end if;
end $$;

do $$
begin
    create index if not exists idx_text_embeddings_vector_ivfflat
        on text_embeddings
        using ivfflat (vector vector_cosine_ops)
        with (lists = 100);
exception when others then
    null;
end $$;
