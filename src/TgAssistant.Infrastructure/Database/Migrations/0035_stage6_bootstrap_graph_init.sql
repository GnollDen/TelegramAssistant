create table if not exists bootstrap_graph_nodes (
    id uuid primary key,
    scope_key text not null,
    person_id uuid references persons(id) on delete set null,
    last_model_pass_run_id uuid references model_pass_runs(id) on delete set null,
    node_type text not null,
    node_ref text not null,
    status text not null default 'active',
    payload_json jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint uq_bootstrap_graph_nodes unique (scope_key, node_type, node_ref)
);

create index if not exists idx_bootstrap_graph_nodes_scope_type
    on bootstrap_graph_nodes(scope_key, node_type, status);

create index if not exists idx_bootstrap_graph_nodes_person
    on bootstrap_graph_nodes(scope_key, person_id, node_type)
    where person_id is not null;

create index if not exists idx_bootstrap_graph_nodes_last_run
    on bootstrap_graph_nodes(last_model_pass_run_id)
    where last_model_pass_run_id is not null;

create table if not exists bootstrap_graph_edges (
    id uuid primary key,
    scope_key text not null,
    last_model_pass_run_id uuid references model_pass_runs(id) on delete set null,
    from_node_ref text not null,
    to_node_ref text not null,
    edge_type text not null,
    status text not null default 'active',
    payload_json jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint uq_bootstrap_graph_edges unique (scope_key, from_node_ref, to_node_ref, edge_type)
);

create index if not exists idx_bootstrap_graph_edges_scope_type
    on bootstrap_graph_edges(scope_key, edge_type, status);

create index if not exists idx_bootstrap_graph_edges_last_run
    on bootstrap_graph_edges(last_model_pass_run_id)
    where last_model_pass_run_id is not null;
