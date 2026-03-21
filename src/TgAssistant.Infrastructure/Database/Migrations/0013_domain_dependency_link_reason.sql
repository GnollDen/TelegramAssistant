alter table if exists domain_dependency_links
    add column if not exists link_reason text;
