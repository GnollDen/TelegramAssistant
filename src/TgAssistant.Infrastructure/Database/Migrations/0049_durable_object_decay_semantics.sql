alter table if exists durable_object_metadata
    add column if not exists decay_class text not null default 'situational_state';

alter table if exists durable_object_metadata
    add column if not exists decay_policy_json jsonb not null default '{}'::jsonb;

update durable_object_metadata
set
    decay_class = case object_family
        when 'dossier' then 'stable_trait'
        when 'profile' then 'stable_trait'
        when 'pair_dynamics' then 'situational_state'
        when 'story_arc' then 'situational_state'
        when 'event' then 'local_episode'
        when 'timeline_episode' then 'local_episode'
        else decay_class
    end,
    decay_policy_json = case object_family
        when 'dossier' then jsonb_build_object(
            'object_family', 'dossier',
            'decay_class', 'stable_trait',
            'fresh_for_days', 30,
            'review_after_days', 90,
            'expire_after_days', 365,
            'decay_strategy', 'slow_decay',
            'policy_note', 'Person-level dossier facts remain durable unless contradicted or explicitly invalidated.')
        when 'profile' then jsonb_build_object(
            'object_family', 'profile',
            'decay_class', 'stable_trait',
            'fresh_for_days', 21,
            'review_after_days', 60,
            'expire_after_days', 180,
            'decay_strategy', 'slow_decay',
            'policy_note', 'Behavioral profile traits persist longer than situational states but still require periodic review.')
        when 'pair_dynamics' then jsonb_build_object(
            'object_family', 'pair_dynamics',
            'decay_class', 'situational_state',
            'fresh_for_days', 10,
            'review_after_days', 30,
            'expire_after_days', 90,
            'decay_strategy', 'context_sensitive',
            'policy_note', 'Relationship dynamics stay durable within a context window and should down-rank faster than traits.')
        when 'event' then jsonb_build_object(
            'object_family', 'event',
            'decay_class', 'local_episode',
            'fresh_for_days', 2,
            'review_after_days', 7,
            'expire_after_days', 30,
            'decay_strategy', 'episode_bound',
            'policy_note', 'Single events should become historical context quickly and must not behave like stable traits.')
        when 'timeline_episode' then jsonb_build_object(
            'object_family', 'timeline_episode',
            'decay_class', 'local_episode',
            'fresh_for_days', 5,
            'review_after_days', 14,
            'expire_after_days', 45,
            'decay_strategy', 'episode_bound',
            'policy_note', 'Local episodes remain relevant for a short narrative window before yielding to newer evidence.')
        when 'story_arc' then jsonb_build_object(
            'object_family', 'story_arc',
            'decay_class', 'situational_state',
            'fresh_for_days', 14,
            'review_after_days', 45,
            'expire_after_days', 120,
            'decay_strategy', 'context_sensitive',
            'policy_note', 'Story arcs outlive local episodes but should still decay faster than stable person traits.')
        else decay_policy_json
    end
where object_family in ('dossier', 'profile', 'pair_dynamics', 'event', 'timeline_episode', 'story_arc');

create index if not exists idx_durable_object_metadata_scope_decay_class_updated
    on durable_object_metadata(scope_key, decay_class, updated_at);
