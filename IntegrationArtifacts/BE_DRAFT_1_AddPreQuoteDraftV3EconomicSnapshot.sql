CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;
DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'core') THEN
        CREATE SCHEMA core;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'identity') THEN
        CREATE SCHEMA identity;
    END IF;
END $EF$;

CREATE TABLE core.clients (
    id uuid NOT NULL,
    client_type character varying(20) NOT NULL,
    legal_name character varying(200) NOT NULL,
    trade_name character varying(200),
    document_type character varying(30),
    document_number character varying(50),
    email character varying(320),
    phone character varying(50),
    address character varying(300),
    city character varying(100),
    is_active boolean NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    updated_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT "PK_clients" PRIMARY KEY (id)
);

CREATE TABLE identity.users (
    id uuid NOT NULL,
    email character varying(320) NOT NULL,
    first_name character varying(100) NOT NULL,
    last_name character varying(100),
    profile_picture_url character varying(2048),
    is_active boolean NOT NULL,
    last_login_at_utc timestamp with time zone,
    created_at_utc timestamp with time zone NOT NULL,
    updated_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT "PK_users" PRIMARY KEY (id)
);

CREATE TABLE identity.external_identities (
    id uuid NOT NULL,
    user_id uuid NOT NULL,
    provider character varying(50) NOT NULL,
    provider_subject character varying(255) NOT NULL,
    provider_email character varying(320) NOT NULL,
    email_verified boolean NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    last_used_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT "PK_external_identities" PRIMARY KEY (id),
    CONSTRAINT "FK_external_identities_users_user_id" FOREIGN KEY (user_id) REFERENCES identity.users (id) ON DELETE CASCADE
);

CREATE TABLE core.projects (
    id uuid NOT NULL,
    client_id uuid NOT NULL,
    code character varying(30) NOT NULL,
    name character varying(200) NOT NULL,
    description character varying(1000),
    location character varying(250),
    created_by_user_id uuid NOT NULL,
    is_active boolean NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    updated_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT "PK_projects" PRIMARY KEY (id),
    CONSTRAINT "FK_projects_clients_client_id" FOREIGN KEY (client_id) REFERENCES core.clients (id) ON DELETE RESTRICT,
    CONSTRAINT "FK_projects_users_created_by_user_id" FOREIGN KEY (created_by_user_id) REFERENCES identity.users (id) ON DELETE RESTRICT
);

CREATE UNIQUE INDEX ux_clients_document_type_number ON core.clients (document_type, document_number) WHERE "document_type" IS NOT NULL AND "document_number" IS NOT NULL;

CREATE INDEX "IX_external_identities_user_id" ON identity.external_identities (user_id);

CREATE UNIQUE INDEX ux_external_identities_provider_subject ON identity.external_identities (provider, provider_subject);

CREATE INDEX ix_projects_client_id ON core.projects (client_id);

CREATE INDEX ix_projects_created_by_user_id ON core.projects (created_by_user_id);

CREATE UNIQUE INDEX ux_projects_code ON core.projects (code);

CREATE UNIQUE INDEX ux_users_email ON identity.users (email);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260721201020_InitialIdentityAndCore', '10.0.10');

COMMIT;

START TRANSACTION;
ALTER TABLE core.clients ADD created_by_user_id uuid NOT NULL;

CREATE INDEX ix_clients_created_by_user_id ON core.clients (created_by_user_id);

ALTER TABLE core.clients ADD CONSTRAINT "FK_clients_users_created_by_user_id" FOREIGN KEY (created_by_user_id) REFERENCES identity.users (id) ON DELETE RESTRICT;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260722211905_AddClientCreatedByUser', '10.0.10');

COMMIT;

START TRANSACTION;
ALTER TABLE core.projects ADD status_changed_at_utc timestamp with time zone;

ALTER TABLE core.projects ADD status_changed_by_user_id uuid;

ALTER TABLE core.projects ADD updated_by_user_id uuid;

ALTER TABLE core.clients ADD status_changed_at_utc timestamp with time zone;

ALTER TABLE core.clients ADD status_changed_by_user_id uuid;

ALTER TABLE core.clients ADD updated_by_user_id uuid;

UPDATE core.projects
SET updated_by_user_id = created_by_user_id
WHERE updated_by_user_id IS NULL;

UPDATE core.clients
SET updated_by_user_id = created_by_user_id
WHERE updated_by_user_id IS NULL;

ALTER TABLE core.projects ALTER COLUMN updated_by_user_id SET NOT NULL;

ALTER TABLE core.clients ALTER COLUMN updated_by_user_id SET NOT NULL;

CREATE INDEX ix_projects_status_changed_by_user_id ON core.projects (status_changed_by_user_id);

CREATE INDEX ix_projects_updated_by_user_id ON core.projects (updated_by_user_id);

CREATE INDEX ix_clients_status_changed_by_user_id ON core.clients (status_changed_by_user_id);

CREATE INDEX ix_clients_updated_by_user_id ON core.clients (updated_by_user_id);

ALTER TABLE core.clients ADD CONSTRAINT "FK_clients_users_status_changed_by_user_id" FOREIGN KEY (status_changed_by_user_id) REFERENCES identity.users (id) ON DELETE RESTRICT;

ALTER TABLE core.clients ADD CONSTRAINT "FK_clients_users_updated_by_user_id" FOREIGN KEY (updated_by_user_id) REFERENCES identity.users (id) ON DELETE RESTRICT;

ALTER TABLE core.projects ADD CONSTRAINT "FK_projects_users_status_changed_by_user_id" FOREIGN KEY (status_changed_by_user_id) REFERENCES identity.users (id) ON DELETE RESTRICT;

ALTER TABLE core.projects ADD CONSTRAINT "FK_projects_users_updated_by_user_id" FOREIGN KEY (updated_by_user_id) REFERENCES identity.users (id) ON DELETE RESTRICT;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260723151318_AddClientProjectAudit', '10.0.10');

COMMIT;

START TRANSACTION;
CREATE TABLE core.pre_quotes (
    id uuid NOT NULL,
    project_id uuid NOT NULL,
    created_by_user_id uuid NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    updated_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT "PK_pre_quotes" PRIMARY KEY (id),
    CONSTRAINT "FK_pre_quotes_projects_project_id" FOREIGN KEY (project_id) REFERENCES core.projects (id) ON DELETE RESTRICT,
    CONSTRAINT "FK_pre_quotes_users_created_by_user_id" FOREIGN KEY (created_by_user_id) REFERENCES identity.users (id) ON DELETE RESTRICT
);

CREATE TABLE core.pre_quote_documents (
    id uuid NOT NULL,
    pre_quote_id uuid NOT NULL,
    original_file_name character varying(255) NOT NULL,
    content_type character varying(100) NOT NULL,
    size_bytes bigint NOT NULL,
    storage_key character varying(500) NOT NULL,
    created_by_user_id uuid NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT "PK_pre_quote_documents" PRIMARY KEY (id),
    CONSTRAINT ck_pre_quote_documents_size_bytes_positive CHECK ("size_bytes" > 0),
    CONSTRAINT "FK_pre_quote_documents_pre_quotes_pre_quote_id" FOREIGN KEY (pre_quote_id) REFERENCES core.pre_quotes (id) ON DELETE RESTRICT,
    CONSTRAINT "FK_pre_quote_documents_users_created_by_user_id" FOREIGN KEY (created_by_user_id) REFERENCES identity.users (id) ON DELETE RESTRICT
);

CREATE INDEX ix_pre_quote_documents_created_by_user_id ON core.pre_quote_documents (created_by_user_id);

CREATE INDEX ix_pre_quote_documents_pre_quote_id ON core.pre_quote_documents (pre_quote_id);

CREATE UNIQUE INDEX ux_pre_quote_documents_storage_key ON core.pre_quote_documents (storage_key);

CREATE INDEX ix_pre_quotes_created_by_user_id ON core.pre_quotes (created_by_user_id);

CREATE INDEX ix_pre_quotes_project_id ON core.pre_quotes (project_id);

CREATE INDEX ix_pre_quotes_updated_at_utc ON core.pre_quotes (updated_at_utc);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260723203539_AddPreQuoteFoundation', '10.0.10');

COMMIT;

START TRANSACTION;
CREATE TABLE core.document_processing_attempts (
    id uuid NOT NULL,
    pre_quote_document_id uuid NOT NULL,
    requested_by_user_id uuid NOT NULL,
    correlation_id uuid NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    completed_at_utc timestamp with time zone,
    outcome varchar(30),
    error_code varchar(64),
    CONSTRAINT "PK_document_processing_attempts" PRIMARY KEY (id),
    CONSTRAINT ck_document_processing_attempts_final_state CHECK ((("outcome" IS NULL AND "completed_at_utc" IS NULL AND "error_code" IS NULL) OR ("outcome" IS NOT NULL AND "outcome" IN ('Completed', 'RequiresReview') AND "completed_at_utc" IS NOT NULL AND "error_code" IS NULL) OR ("outcome" IS NOT NULL AND "outcome" = 'Failed' AND "completed_at_utc" IS NOT NULL AND "error_code" IS NOT NULL AND "error_code" <> ''))),
    CONSTRAINT "FK_document_processing_attempts_pre_quote_documents_pre_quote_~" FOREIGN KEY (pre_quote_document_id) REFERENCES core.pre_quote_documents (id) ON DELETE RESTRICT,
    CONSTRAINT "FK_document_processing_attempts_users_requested_by_user_id" FOREIGN KEY (requested_by_user_id) REFERENCES identity.users (id) ON DELETE RESTRICT
);

CREATE TABLE core.document_extraction_results (
    id uuid NOT NULL,
    document_processing_attempt_id uuid NOT NULL,
    schema_version varchar(20) NOT NULL,
    classification varchar(30) NOT NULL,
    requires_ocr boolean NOT NULL,
    page_count integer NOT NULL,
    processing_method varchar(100) NOT NULL,
    duration_ms integer NOT NULL,
    payload_json jsonb NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT "PK_document_extraction_results" PRIMARY KEY (id),
    CONSTRAINT ck_document_extraction_results_classification_ocr CHECK ((("classification" = 'PdfText' AND "requires_ocr" = false) OR ("classification" = 'PdfScanned' AND "requires_ocr" = true) OR ("classification" = 'PdfMixed' AND "requires_ocr" = true))),
    CONSTRAINT ck_document_extraction_results_duration_ms_non_negative CHECK ("duration_ms" >= 0),
    CONSTRAINT ck_document_extraction_results_page_count_positive CHECK ("page_count" >= 1),
    CONSTRAINT "FK_document_extraction_results_document_processing_attempts_do~" FOREIGN KEY (document_processing_attempt_id) REFERENCES core.document_processing_attempts (id) ON DELETE RESTRICT
);

CREATE UNIQUE INDEX ux_document_extraction_results_processing_attempt_id ON core.document_extraction_results (document_processing_attempt_id);

CREATE INDEX ix_document_processing_attempts_created_at_utc ON core.document_processing_attempts (created_at_utc);

CREATE INDEX ix_document_processing_attempts_pre_quote_document_id ON core.document_processing_attempts (pre_quote_document_id);

CREATE INDEX ix_document_processing_attempts_requested_by_user_id ON core.document_processing_attempts (requested_by_user_id);

CREATE UNIQUE INDEX ux_document_processing_attempts_correlation_id ON core.document_processing_attempts (correlation_id);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260724155013_AddDocumentProcessingFoundation', '10.0.10');

COMMIT;

START TRANSACTION;
DROP INDEX core.ix_document_processing_attempts_pre_quote_document_id;

ALTER TABLE core.document_processing_attempts DROP CONSTRAINT ck_document_processing_attempts_final_state;

ALTER TABLE core.document_processing_attempts ADD processing_state varchar(20);

ALTER TABLE core.document_processing_attempts ADD started_at_utc timestamp with time zone;

UPDATE core.document_processing_attempts
SET processing_state = CASE
        WHEN outcome IS NULL THEN 'Pending'
        ELSE 'Finished'
    END,
    started_at_utc = CASE
        WHEN outcome IS NULL THEN NULL
        ELSE created_at_utc
    END;

DO $$
BEGIN
    IF EXISTS (
        SELECT pre_quote_document_id
        FROM core.document_processing_attempts
        WHERE processing_state IN ('Pending', 'Processing')
        GROUP BY pre_quote_document_id
        HAVING COUNT(*) > 1
    ) THEN
        RAISE EXCEPTION
            'No se puede crear el índice de intento activo: existen documentos con múltiples intentos abiertos.';
    END IF;
END
$$;

ALTER TABLE core.document_processing_attempts ALTER COLUMN processing_state SET NOT NULL;

CREATE INDEX ix_document_processing_attempts_processing_state_created_at_utc ON core.document_processing_attempts (processing_state, created_at_utc);

CREATE UNIQUE INDEX ux_document_processing_attempts_active_pre_quote_document_id ON core.document_processing_attempts (pre_quote_document_id) WHERE "processing_state" IN ('Pending', 'Processing');

ALTER TABLE core.document_processing_attempts ADD CONSTRAINT ck_document_processing_attempts_lifecycle CHECK ((("processing_state" = 'Pending' AND "started_at_utc" IS NULL AND "outcome" IS NULL AND "completed_at_utc" IS NULL AND "error_code" IS NULL) OR ("processing_state" = 'Processing' AND "started_at_utc" IS NOT NULL AND "started_at_utc" >= "created_at_utc" AND "outcome" IS NULL AND "completed_at_utc" IS NULL AND "error_code" IS NULL) OR ("processing_state" = 'Finished' AND "started_at_utc" IS NOT NULL AND "started_at_utc" >= "created_at_utc" AND "completed_at_utc" IS NOT NULL AND "completed_at_utc" >= "started_at_utc" AND (("outcome" IN ('Completed', 'RequiresReview') AND "error_code" IS NULL) OR ("outcome" = 'Failed' AND "error_code" IS NOT NULL AND "error_code" <> '')))));

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260728163519_AddDocumentProcessingLifecycle', '10.0.10');

COMMIT;

START TRANSACTION;
CREATE TABLE core.structured_document_extractions (
    id uuid NOT NULL,
    document_extraction_result_id uuid NOT NULL,
    status varchar(30) NOT NULL,
    project_name text,
    client_name text,
    location text,
    item_count integer NOT NULL,
    document_reference_count integer NOT NULL,
    items_requiring_review integer NOT NULL,
    known_quoteable_unit_count integer NOT NULL,
    processing_method varchar(100) NOT NULL,
    duration_ms integer NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT "PK_structured_document_extractions" PRIMARY KEY (id),
    CONSTRAINT ck_structured_extractions_counts CHECK ("item_count" >= 0 AND "document_reference_count" >= 0 AND "items_requiring_review" >= 0 AND "known_quoteable_unit_count" >= 0),
    CONSTRAINT ck_structured_extractions_duration CHECK ("duration_ms" >= 0),
    CONSTRAINT "FK_structured_document_extractions_document_extraction_results~" FOREIGN KEY (document_extraction_result_id) REFERENCES core.document_extraction_results (id) ON DELETE CASCADE
);

CREATE TABLE core.structured_extraction_conflicts (
    id uuid NOT NULL,
    structured_document_extraction_id uuid NOT NULL,
    sequence integer NOT NULL,
    code varchar(80) NOT NULL,
    message text NOT NULL,
    item_sequences integer[] NOT NULL,
    page_numbers integer[] NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT "PK_structured_extraction_conflicts" PRIMARY KEY (id),
    CONSTRAINT "FK_structured_extraction_conflicts_structured_document_extract~" FOREIGN KEY (structured_document_extraction_id) REFERENCES core.structured_document_extractions (id) ON DELETE CASCADE
);

CREATE TABLE core.structured_extraction_document_references (
    id uuid NOT NULL,
    structured_document_extraction_id uuid NOT NULL,
    sequence integer NOT NULL,
    reference text,
    description text NOT NULL,
    detail text,
    quantity integer,
    created_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT "PK_structured_extraction_document_references" PRIMARY KEY (id),
    CONSTRAINT ck_structured_extraction_document_references_quantity_positive CHECK ("quantity" IS NULL OR "quantity" > 0),
    CONSTRAINT "FK_structured_extraction_document_references_structured_docume~" FOREIGN KEY (structured_document_extraction_id) REFERENCES core.structured_document_extractions (id) ON DELETE CASCADE
);

CREATE TABLE core.structured_extraction_issues (
    id uuid NOT NULL,
    structured_document_extraction_id uuid NOT NULL,
    sequence integer NOT NULL,
    code varchar(80) NOT NULL,
    message text NOT NULL,
    item_sequence integer,
    page_numbers integer[] NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT "PK_structured_extraction_issues" PRIMARY KEY (id),
    CONSTRAINT "FK_structured_extraction_issues_structured_document_extraction~" FOREIGN KEY (structured_document_extraction_id) REFERENCES core.structured_document_extractions (id) ON DELETE CASCADE
);

CREATE TABLE core.structured_extraction_items (
    id uuid NOT NULL,
    structured_document_extraction_id uuid NOT NULL,
    sequence integer NOT NULL,
    reference text,
    description text NOT NULL,
    element_type varchar(30) NOT NULL,
    raw_measurements text,
    width_millimeters integer,
    height_millimeters integer,
    quantity integer,
    requires_review boolean NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT "PK_structured_extraction_items" PRIMARY KEY (id),
    CONSTRAINT ck_structured_items_values CHECK (("width_millimeters" IS NULL AND "height_millimeters" IS NULL OR "width_millimeters" > 0 AND "height_millimeters" > 0) AND ("quantity" IS NULL OR "quantity" > 0) AND "sequence" > 0),
    CONSTRAINT "FK_structured_extraction_items_structured_document_extractions~" FOREIGN KEY (structured_document_extraction_id) REFERENCES core.structured_document_extractions (id) ON DELETE CASCADE
);

CREATE TABLE core.structured_extraction_requirements (
    id uuid NOT NULL,
    structured_document_extraction_id uuid NOT NULL,
    sequence integer NOT NULL,
    category varchar(50) NOT NULL,
    value text NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT "PK_structured_extraction_requirements" PRIMARY KEY (id),
    CONSTRAINT "FK_structured_extraction_requirements_structured_document_extr~" FOREIGN KEY (structured_document_extraction_id) REFERENCES core.structured_document_extractions (id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX "IX_structured_document_extractions_document_extraction_result_~" ON core.structured_document_extractions (document_extraction_result_id);

CREATE UNIQUE INDEX "IX_structured_extraction_conflicts_structured_document_extract~" ON core.structured_extraction_conflicts (structured_document_extraction_id, sequence);

CREATE UNIQUE INDEX "IX_structured_extraction_document_references_structured_docume~" ON core.structured_extraction_document_references (structured_document_extraction_id, sequence);

CREATE UNIQUE INDEX "IX_structured_extraction_issues_structured_document_extraction~" ON core.structured_extraction_issues (structured_document_extraction_id, sequence);

CREATE UNIQUE INDEX "IX_structured_extraction_items_structured_document_extraction_~" ON core.structured_extraction_items (structured_document_extraction_id, sequence);

CREATE UNIQUE INDEX "IX_structured_extraction_requirements_structured_document_extr~" ON core.structured_extraction_requirements (structured_document_extraction_id, sequence);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260729190313_AddStructuredDocumentExtractions', '10.0.10');

COMMIT;

START TRANSACTION;
CREATE TABLE core.pre_quote_drafts (
    id uuid NOT NULL,
    pre_quote_id uuid NOT NULL,
    source_document_id uuid NOT NULL,
    source_structured_extraction_id uuid NOT NULL,
    status character varying(30) NOT NULL,
    project_name character varying(500),
    client_name character varying(500),
    location character varying(500),
    version integer NOT NULL,
    created_by_user_id uuid NOT NULL,
    updated_by_user_id uuid NOT NULL,
    approved_by_user_id uuid,
    created_at_utc timestamp with time zone NOT NULL,
    updated_at_utc timestamp with time zone NOT NULL,
    approved_at_utc timestamp with time zone,
    CONSTRAINT "PK_pre_quote_drafts" PRIMARY KEY (id),
    CONSTRAINT ck_pre_quote_drafts_approval CHECK (("status" = 'Approved' AND "approved_by_user_id" IS NOT NULL AND "approved_at_utc" IS NOT NULL) OR ("status" <> 'Approved' AND "approved_by_user_id" IS NULL AND "approved_at_utc" IS NULL)),
    CONSTRAINT ck_pre_quote_drafts_version CHECK ("version" > 0),
    CONSTRAINT "FK_pre_quote_drafts_pre_quote_documents_source_document_id" FOREIGN KEY (source_document_id) REFERENCES core.pre_quote_documents (id) ON DELETE RESTRICT,
    CONSTRAINT "FK_pre_quote_drafts_pre_quotes_pre_quote_id" FOREIGN KEY (pre_quote_id) REFERENCES core.pre_quotes (id) ON DELETE RESTRICT,
    CONSTRAINT "FK_pre_quote_drafts_structured_document_extractions_source_str~" FOREIGN KEY (source_structured_extraction_id) REFERENCES core.structured_document_extractions (id) ON DELETE RESTRICT,
    CONSTRAINT "FK_pre_quote_drafts_users_approved_by_user_id" FOREIGN KEY (approved_by_user_id) REFERENCES identity.users (id) ON DELETE RESTRICT,
    CONSTRAINT "FK_pre_quote_drafts_users_created_by_user_id" FOREIGN KEY (created_by_user_id) REFERENCES identity.users (id) ON DELETE RESTRICT,
    CONSTRAINT "FK_pre_quote_drafts_users_updated_by_user_id" FOREIGN KEY (updated_by_user_id) REFERENCES identity.users (id) ON DELETE RESTRICT
);

CREATE TABLE core.pre_quote_draft_conflicts (
    id uuid NOT NULL,
    source_structured_conflict_id uuid NOT NULL,
    source_conflict_sequence integer NOT NULL,
    code character varying(80) NOT NULL,
    message text NOT NULL,
    item_sequences integer[] NOT NULL,
    page_numbers integer[] NOT NULL,
    pre_quote_draft_id uuid NOT NULL,
    sequence integer NOT NULL,
    resolution_status character varying(20) NOT NULL,
    resolution_note character varying(2000),
    resolved_by_user_id uuid,
    resolved_at_utc timestamp with time zone,
    created_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT "PK_pre_quote_draft_conflicts" PRIMARY KEY (id),
    CONSTRAINT ck_pre_quote_draft_conflicts_resolution CHECK (("resolution_status" = 'Pending' AND "resolution_note" IS NULL AND "resolved_by_user_id" IS NULL AND "resolved_at_utc" IS NULL) OR ("resolution_status" IN ('Resolved','Dismissed') AND "resolution_note" IS NOT NULL AND "resolved_by_user_id" IS NOT NULL AND "resolved_at_utc" IS NOT NULL)),
    CONSTRAINT ck_pre_quote_draft_conflicts_sequence CHECK ("sequence" > 0),
    CONSTRAINT "FK_pre_quote_draft_conflicts_pre_quote_drafts_pre_quote_draft_~" FOREIGN KEY (pre_quote_draft_id) REFERENCES core.pre_quote_drafts (id) ON DELETE CASCADE,
    CONSTRAINT "FK_pre_quote_draft_conflicts_structured_extraction_conflicts_s~" FOREIGN KEY (source_structured_conflict_id) REFERENCES core.structured_extraction_conflicts (id) ON DELETE RESTRICT,
    CONSTRAINT "FK_pre_quote_draft_conflicts_users_resolved_by_user_id" FOREIGN KEY (resolved_by_user_id) REFERENCES identity.users (id) ON DELETE RESTRICT
);

CREATE TABLE core.pre_quote_draft_document_references (
    id uuid NOT NULL,
    pre_quote_draft_id uuid NOT NULL,
    sequence integer NOT NULL,
    origin character varying(20) NOT NULL,
    source_structured_document_reference_id uuid,
    source_document_reference_sequence integer,
    reference character varying(200),
    description character varying(1000) NOT NULL,
    detail character varying(2000),
    quantity integer,
    is_included boolean NOT NULL,
    created_by_user_id uuid NOT NULL,
    updated_by_user_id uuid NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    updated_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT "PK_pre_quote_draft_document_references" PRIMARY KEY (id),
    CONSTRAINT ck_pre_quote_draft_document_references_origin CHECK (("origin" = 'Ai' AND "source_structured_document_reference_id" IS NOT NULL AND "source_document_reference_sequence" IS NOT NULL) OR ("origin" = 'Manual' AND "source_structured_document_reference_id" IS NULL AND "source_document_reference_sequence" IS NULL)),
    CONSTRAINT ck_pre_quote_draft_document_references_quantity CHECK ("quantity" IS NULL OR "quantity" > 0),
    CONSTRAINT ck_pre_quote_draft_document_references_sequence CHECK ("sequence" > 0),
    CONSTRAINT "FK_pre_quote_draft_document_references_pre_quote_drafts_pre_qu~" FOREIGN KEY (pre_quote_draft_id) REFERENCES core.pre_quote_drafts (id) ON DELETE CASCADE,
    CONSTRAINT "FK_pre_quote_draft_document_references_structured_extraction_d~" FOREIGN KEY (source_structured_document_reference_id) REFERENCES core.structured_extraction_document_references (id) ON DELETE RESTRICT
);

CREATE TABLE core.pre_quote_draft_issues (
    id uuid NOT NULL,
    source_structured_issue_id uuid NOT NULL,
    source_issue_sequence integer NOT NULL,
    code character varying(80) NOT NULL,
    message text NOT NULL,
    item_sequence integer,
    page_numbers integer[] NOT NULL,
    pre_quote_draft_id uuid NOT NULL,
    sequence integer NOT NULL,
    resolution_status character varying(20) NOT NULL,
    resolution_note character varying(2000),
    resolved_by_user_id uuid,
    resolved_at_utc timestamp with time zone,
    created_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT "PK_pre_quote_draft_issues" PRIMARY KEY (id),
    CONSTRAINT ck_pre_quote_draft_issues_resolution CHECK (("resolution_status" = 'Pending' AND "resolution_note" IS NULL AND "resolved_by_user_id" IS NULL AND "resolved_at_utc" IS NULL) OR ("resolution_status" IN ('Resolved','Dismissed') AND "resolution_note" IS NOT NULL AND "resolved_by_user_id" IS NOT NULL AND "resolved_at_utc" IS NOT NULL)),
    CONSTRAINT ck_pre_quote_draft_issues_sequence CHECK ("sequence" > 0),
    CONSTRAINT "FK_pre_quote_draft_issues_pre_quote_drafts_pre_quote_draft_id" FOREIGN KEY (pre_quote_draft_id) REFERENCES core.pre_quote_drafts (id) ON DELETE CASCADE,
    CONSTRAINT "FK_pre_quote_draft_issues_structured_extraction_issues_source_~" FOREIGN KEY (source_structured_issue_id) REFERENCES core.structured_extraction_issues (id) ON DELETE RESTRICT,
    CONSTRAINT "FK_pre_quote_draft_issues_users_resolved_by_user_id" FOREIGN KEY (resolved_by_user_id) REFERENCES identity.users (id) ON DELETE RESTRICT
);

CREATE TABLE core.pre_quote_draft_items (
    id uuid NOT NULL,
    pre_quote_draft_id uuid NOT NULL,
    sequence integer NOT NULL,
    origin character varying(20) NOT NULL,
    source_structured_item_id uuid,
    source_item_sequence integer,
    reference character varying(200),
    description character varying(1000) NOT NULL,
    element_type character varying(30) NOT NULL,
    raw_measurements character varying(500),
    width_millimeters integer,
    height_millimeters integer,
    quantity integer,
    is_included boolean NOT NULL,
    created_by_user_id uuid NOT NULL,
    updated_by_user_id uuid NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    updated_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT "PK_pre_quote_draft_items" PRIMARY KEY (id),
    CONSTRAINT ck_pre_quote_draft_items_origin CHECK (("origin" = 'Ai' AND "source_structured_item_id" IS NOT NULL AND "source_item_sequence" IS NOT NULL) OR ("origin" = 'Manual' AND "source_structured_item_id" IS NULL AND "source_item_sequence" IS NULL)),
    CONSTRAINT ck_pre_quote_draft_items_sequence CHECK ("sequence" > 0),
    CONSTRAINT ck_pre_quote_draft_items_values CHECK (("width_millimeters" IS NULL AND "height_millimeters" IS NULL OR "width_millimeters" > 0 AND "height_millimeters" > 0) AND ("quantity" IS NULL OR "quantity" > 0)),
    CONSTRAINT "FK_pre_quote_draft_items_pre_quote_drafts_pre_quote_draft_id" FOREIGN KEY (pre_quote_draft_id) REFERENCES core.pre_quote_drafts (id) ON DELETE CASCADE,
    CONSTRAINT "FK_pre_quote_draft_items_structured_extraction_items_source_st~" FOREIGN KEY (source_structured_item_id) REFERENCES core.structured_extraction_items (id) ON DELETE RESTRICT
);

CREATE TABLE core.pre_quote_draft_requirements (
    id uuid NOT NULL,
    pre_quote_draft_id uuid NOT NULL,
    sequence integer NOT NULL,
    origin character varying(20) NOT NULL,
    source_structured_requirement_id uuid,
    source_requirement_sequence integer,
    category character varying(50) NOT NULL,
    value character varying(1000) NOT NULL,
    is_included boolean NOT NULL,
    created_by_user_id uuid NOT NULL,
    updated_by_user_id uuid NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    updated_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT "PK_pre_quote_draft_requirements" PRIMARY KEY (id),
    CONSTRAINT ck_pre_quote_draft_requirements_origin CHECK (("origin" = 'Ai' AND "source_structured_requirement_id" IS NOT NULL AND "source_requirement_sequence" IS NOT NULL) OR ("origin" = 'Manual' AND "source_structured_requirement_id" IS NULL AND "source_requirement_sequence" IS NULL)),
    CONSTRAINT ck_pre_quote_draft_requirements_sequence CHECK ("sequence" > 0),
    CONSTRAINT "FK_pre_quote_draft_requirements_pre_quote_drafts_pre_quote_dra~" FOREIGN KEY (pre_quote_draft_id) REFERENCES core.pre_quote_drafts (id) ON DELETE CASCADE,
    CONSTRAINT "FK_pre_quote_draft_requirements_structured_extraction_requirem~" FOREIGN KEY (source_structured_requirement_id) REFERENCES core.structured_extraction_requirements (id) ON DELETE RESTRICT
);

CREATE UNIQUE INDEX "IX_pre_quote_draft_conflicts_pre_quote_draft_id_sequence" ON core.pre_quote_draft_conflicts (pre_quote_draft_id, sequence);

CREATE INDEX "IX_pre_quote_draft_conflicts_resolved_by_user_id" ON core.pre_quote_draft_conflicts (resolved_by_user_id);

CREATE INDEX "IX_pre_quote_draft_conflicts_source_structured_conflict_id" ON core.pre_quote_draft_conflicts (source_structured_conflict_id);

CREATE UNIQUE INDEX "IX_pre_quote_draft_document_references_pre_quote_draft_id_sequ~" ON core.pre_quote_draft_document_references (pre_quote_draft_id, sequence);

CREATE INDEX "IX_pre_quote_draft_document_references_source_structured_docum~" ON core.pre_quote_draft_document_references (source_structured_document_reference_id);

CREATE UNIQUE INDEX "IX_pre_quote_draft_issues_pre_quote_draft_id_sequence" ON core.pre_quote_draft_issues (pre_quote_draft_id, sequence);

CREATE INDEX "IX_pre_quote_draft_issues_resolved_by_user_id" ON core.pre_quote_draft_issues (resolved_by_user_id);

CREATE INDEX "IX_pre_quote_draft_issues_source_structured_issue_id" ON core.pre_quote_draft_issues (source_structured_issue_id);

CREATE UNIQUE INDEX "IX_pre_quote_draft_items_pre_quote_draft_id_sequence" ON core.pre_quote_draft_items (pre_quote_draft_id, sequence);

CREATE INDEX "IX_pre_quote_draft_items_source_structured_item_id" ON core.pre_quote_draft_items (source_structured_item_id);

CREATE UNIQUE INDEX "IX_pre_quote_draft_requirements_pre_quote_draft_id_sequence" ON core.pre_quote_draft_requirements (pre_quote_draft_id, sequence);

CREATE INDEX "IX_pre_quote_draft_requirements_source_structured_requirement_~" ON core.pre_quote_draft_requirements (source_structured_requirement_id);

CREATE INDEX "IX_pre_quote_drafts_approved_by_user_id" ON core.pre_quote_drafts (approved_by_user_id);

CREATE INDEX "IX_pre_quote_drafts_created_by_user_id" ON core.pre_quote_drafts (created_by_user_id);

CREATE UNIQUE INDEX "IX_pre_quote_drafts_pre_quote_id" ON core.pre_quote_drafts (pre_quote_id);

CREATE INDEX "IX_pre_quote_drafts_source_document_id" ON core.pre_quote_drafts (source_document_id);

CREATE INDEX "IX_pre_quote_drafts_source_structured_extraction_id" ON core.pre_quote_drafts (source_structured_extraction_id);

CREATE INDEX "IX_pre_quote_drafts_updated_by_user_id" ON core.pre_quote_drafts (updated_by_user_id);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260730121404_AddPreQuoteDrafts', '10.0.10');

COMMIT;

START TRANSACTION;
CREATE TABLE core.glass_types (
    id uuid NOT NULL,
    code character varying(30) NOT NULL,
    name character varying(100) NOT NULL,
    description character varying(500),
    is_active boolean NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    updated_at_utc timestamp with time zone,
    CONSTRAINT "PK_glass_types" PRIMARY KEY (id)
);

CREATE TABLE core.glass_price_range_versions (
    id uuid NOT NULL,
    glass_type_id uuid NOT NULL,
    version integer NOT NULL,
    minimum_price_per_square_meter numeric(18,2) NOT NULL,
    maximum_price_per_square_meter numeric(18,2) NOT NULL,
    currency character varying(3) NOT NULL,
    status character varying(20) NOT NULL,
    valid_from_utc timestamp with time zone NOT NULL,
    valid_to_utc timestamp with time zone,
    created_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT "PK_glass_price_range_versions" PRIMARY KEY (id),
    CONSTRAINT ck_glass_price_range_versions_maximum_price CHECK ("maximum_price_per_square_meter" >= "minimum_price_per_square_meter"),
    CONSTRAINT ck_glass_price_range_versions_minimum_price CHECK ("minimum_price_per_square_meter" > 0),
    CONSTRAINT ck_glass_price_range_versions_validity CHECK ("valid_to_utc" IS NULL OR "valid_to_utc" > "valid_from_utc"),
    CONSTRAINT ck_glass_price_range_versions_version CHECK ("version" > 0),
    CONSTRAINT "FK_glass_price_range_versions_glass_types_glass_type_id" FOREIGN KEY (glass_type_id) REFERENCES core.glass_types (id) ON DELETE RESTRICT
);

INSERT INTO core.glass_types (id, code, created_at_utc, description, is_active, name, updated_at_utc)
VALUES ('10000000-0000-0000-0000-000000000001', 'LAM_4_4', TIMESTAMPTZ '2026-07-31T00:00:00+00:00', NULL, TRUE, 'Laminado 4+4', NULL);
INSERT INTO core.glass_types (id, code, created_at_utc, description, is_active, name, updated_at_utc)
VALUES ('10000000-0000-0000-0000-000000000002', 'LAM_4_4_GRAY', TIMESTAMPTZ '2026-07-31T00:00:00+00:00', NULL, TRUE, 'Laminado 4+4 gris', NULL);
INSERT INTO core.glass_types (id, code, created_at_utc, description, is_active, name, updated_at_utc)
VALUES ('10000000-0000-0000-0000-000000000003', 'LAM_5_5', TIMESTAMPTZ '2026-07-31T00:00:00+00:00', NULL, TRUE, 'Laminado 5+5', NULL);
INSERT INTO core.glass_types (id, code, created_at_utc, description, is_active, name, updated_at_utc)
VALUES ('10000000-0000-0000-0000-000000000004', 'LAM_5_5_GRAY', TIMESTAMPTZ '2026-07-31T00:00:00+00:00', NULL, TRUE, 'Laminado 5+5 gris', NULL);

INSERT INTO core.glass_price_range_versions (id, created_at_utc, currency, glass_type_id, maximum_price_per_square_meter, minimum_price_per_square_meter, status, valid_from_utc, valid_to_utc, version)
VALUES ('20000000-0000-0000-0000-000000000001', TIMESTAMPTZ '2026-07-31T00:00:00+00:00', 'COP', '10000000-0000-0000-0000-000000000001', 110000.0, 90000.0, 'PRELIMINARY', TIMESTAMPTZ '2026-07-31T00:00:00+00:00', NULL, 1);
INSERT INTO core.glass_price_range_versions (id, created_at_utc, currency, glass_type_id, maximum_price_per_square_meter, minimum_price_per_square_meter, status, valid_from_utc, valid_to_utc, version)
VALUES ('20000000-0000-0000-0000-000000000002', TIMESTAMPTZ '2026-07-31T00:00:00+00:00', 'COP', '10000000-0000-0000-0000-000000000002', 95000.0, 95000.0, 'PRELIMINARY', TIMESTAMPTZ '2026-07-31T00:00:00+00:00', NULL, 1);
INSERT INTO core.glass_price_range_versions (id, created_at_utc, currency, glass_type_id, maximum_price_per_square_meter, minimum_price_per_square_meter, status, valid_from_utc, valid_to_utc, version)
VALUES ('20000000-0000-0000-0000-000000000003', TIMESTAMPTZ '2026-07-31T00:00:00+00:00', 'COP', '10000000-0000-0000-0000-000000000003', 140000.0, 120000.0, 'PRELIMINARY', TIMESTAMPTZ '2026-07-31T00:00:00+00:00', NULL, 1);
INSERT INTO core.glass_price_range_versions (id, created_at_utc, currency, glass_type_id, maximum_price_per_square_meter, minimum_price_per_square_meter, status, valid_from_utc, valid_to_utc, version)
VALUES ('20000000-0000-0000-0000-000000000004', TIMESTAMPTZ '2026-07-31T00:00:00+00:00', 'COP', '10000000-0000-0000-0000-000000000004', 145000.0, 125000.0, 'PRELIMINARY', TIMESTAMPTZ '2026-07-31T00:00:00+00:00', NULL, 1);

CREATE INDEX ix_glass_price_range_versions_glass_type_id_valid_to_utc ON core.glass_price_range_versions (glass_type_id, valid_to_utc);

CREATE UNIQUE INDEX ux_glass_price_range_versions_open_type ON core.glass_price_range_versions (glass_type_id) WHERE "valid_to_utc" IS NULL;

CREATE UNIQUE INDEX ux_glass_price_range_versions_type_version ON core.glass_price_range_versions (glass_type_id, version);

CREATE UNIQUE INDEX ux_glass_types_code ON core.glass_types (code);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260731154117_AddGlassTypesAndPriceRanges', '10.0.10');

COMMIT;

START TRANSACTION;
ALTER TABLE core.structured_document_extractions ADD glass_items_requiring_review integer;

ALTER TABLE core.structured_document_extractions ADD identified_glass_item_count integer;

CREATE TABLE core.structured_extraction_item_glass_detections (
    id uuid NOT NULL,
    structured_extraction_item_id uuid NOT NULL,
    glass_type_id uuid,
    raw_specification character varying(500),
    normalized_code_snapshot character varying(30),
    assignment_scope character varying(20) NOT NULL,
    requires_review boolean NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT "PK_structured_extraction_item_glass_detections" PRIMARY KEY (id),
    CONSTRAINT ck_structured_item_glass_detection_identity CHECK (("normalized_code_snapshot" IS NULL AND "glass_type_id" IS NULL) OR ("normalized_code_snapshot" IS NOT NULL AND "glass_type_id" IS NOT NULL)),
    CONSTRAINT ck_structured_item_glass_detection_scope CHECK ("assignment_scope" IN ('Item', 'Section', 'General', 'Unassigned')),
    CONSTRAINT "FK_structured_extraction_item_glass_detections_glass_types_gla~" FOREIGN KEY (glass_type_id) REFERENCES core.glass_types (id) ON DELETE RESTRICT,
    CONSTRAINT "FK_structured_extraction_item_glass_detections_structured_extr~" FOREIGN KEY (structured_extraction_item_id) REFERENCES core.structured_extraction_items (id) ON DELETE CASCADE
);

CREATE TABLE core.structured_extraction_item_glass_evidence (
    id uuid NOT NULL,
    glass_detection_id uuid NOT NULL,
    sequence integer NOT NULL,
    page_number integer NOT NULL,
    source_type character varying(10) NOT NULL,
    text character varying(500) NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT "PK_structured_extraction_item_glass_evidence" PRIMARY KEY (id),
    CONSTRAINT ck_structured_item_glass_evidence_page_positive CHECK ("page_number" > 0),
    CONSTRAINT ck_structured_item_glass_evidence_sequence CHECK ("sequence" > 0),
    CONSTRAINT ck_structured_item_glass_evidence_source_type CHECK ("source_type" IN ('Native', 'Ocr')),
    CONSTRAINT "FK_structured_extraction_item_glass_evidence_structured_extrac~" FOREIGN KEY (glass_detection_id) REFERENCES core.structured_extraction_item_glass_detections (id) ON DELETE CASCADE
);

CREATE TABLE core.structured_extraction_item_glass_review_reasons (
    id uuid NOT NULL,
    glass_detection_id uuid NOT NULL,
    sequence integer NOT NULL,
    code character varying(40) NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT "PK_structured_extraction_item_glass_review_reasons" PRIMARY KEY (id),
    CONSTRAINT ck_structured_item_glass_review_reason_code CHECK ("code" IN ('GlassTypeNotIdentified', 'GlassTypeAmbiguous', 'GlassTypeConflict')),
    CONSTRAINT ck_structured_item_glass_review_reason_sequence CHECK ("sequence" > 0),
    CONSTRAINT "FK_structured_extraction_item_glass_review_reasons_structured_~" FOREIGN KEY (glass_detection_id) REFERENCES core.structured_extraction_item_glass_detections (id) ON DELETE CASCADE
);

CREATE TABLE core.structured_extraction_item_glass_source_pages (
    id uuid NOT NULL,
    glass_detection_id uuid NOT NULL,
    sequence integer NOT NULL,
    page_number integer NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT "PK_structured_extraction_item_glass_source_pages" PRIMARY KEY (id),
    CONSTRAINT ck_structured_item_glass_source_page_positive CHECK ("page_number" > 0),
    CONSTRAINT ck_structured_item_glass_source_page_sequence CHECK ("sequence" > 0),
    CONSTRAINT "FK_structured_extraction_item_glass_source_pages_structured_ex~" FOREIGN KEY (glass_detection_id) REFERENCES core.structured_extraction_item_glass_detections (id) ON DELETE CASCADE
);

ALTER TABLE core.structured_document_extractions ADD CONSTRAINT ck_structured_extractions_glass_counts CHECK (("identified_glass_item_count" IS NULL AND "glass_items_requiring_review" IS NULL) OR ("identified_glass_item_count" >= 0 AND "glass_items_requiring_review" >= 0));

CREATE INDEX "IX_structured_extraction_item_glass_detections_glass_type_id" ON core.structured_extraction_item_glass_detections (glass_type_id);

CREATE UNIQUE INDEX "IX_structured_extraction_item_glass_detections_structured_extr~" ON core.structured_extraction_item_glass_detections (structured_extraction_item_id);

CREATE UNIQUE INDEX "IX_structured_extraction_item_glass_evidence_glass_detection_~1" ON core.structured_extraction_item_glass_evidence (glass_detection_id, page_number, source_type, text);

CREATE UNIQUE INDEX "IX_structured_extraction_item_glass_evidence_glass_detection_i~" ON core.structured_extraction_item_glass_evidence (glass_detection_id, sequence);

CREATE UNIQUE INDEX "IX_structured_extraction_item_glass_review_reasons_glass_dete~1" ON core.structured_extraction_item_glass_review_reasons (glass_detection_id, sequence);

CREATE UNIQUE INDEX "IX_structured_extraction_item_glass_review_reasons_glass_detec~" ON core.structured_extraction_item_glass_review_reasons (glass_detection_id, code);

CREATE UNIQUE INDEX "IX_structured_extraction_item_glass_source_pages_glass_detect~1" ON core.structured_extraction_item_glass_source_pages (glass_detection_id, sequence);

CREATE UNIQUE INDEX "IX_structured_extraction_item_glass_source_pages_glass_detecti~" ON core.structured_extraction_item_glass_source_pages (glass_detection_id, page_number);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260731155743_AddStructuredItemGlassDetections', '10.0.10');

COMMIT;

START TRANSACTION;
CREATE TABLE core.structured_extraction_item_glass_valuations (
    id uuid NOT NULL,
    structured_extraction_item_id uuid NOT NULL,
    status character varying(20) NOT NULL,
    reason character varying(40),
    glass_type_id uuid,
    glass_price_range_version_id uuid,
    price_range_version integer,
    price_range_status character varying(20),
    currency character varying(3),
    unit_area_square_meters numeric(18,6),
    total_area_square_meters numeric(18,6),
    minimum_price_per_square_meter numeric(18,2),
    maximum_price_per_square_meter numeric(18,2),
    minimum_amount numeric(18,2),
    maximum_amount numeric(18,2),
    calculated_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT "PK_structured_extraction_item_glass_valuations" PRIMARY KEY (id),
    CONSTRAINT ck_structured_glass_valuation_amounts CHECK ("minimum_amount" IS NULL OR "minimum_amount" >= 0 AND "maximum_amount" >= "minimum_amount"),
    CONSTRAINT ck_structured_glass_valuation_areas CHECK ("unit_area_square_meters" IS NULL OR "unit_area_square_meters" >= 0 AND "total_area_square_meters" >= 0),
    CONSTRAINT ck_structured_glass_valuation_currency CHECK ("currency" IS NULL OR char_length("currency") = 3),
    CONSTRAINT ck_structured_glass_valuation_prices CHECK ("minimum_price_per_square_meter" IS NULL OR "minimum_price_per_square_meter" > 0 AND "maximum_price_per_square_meter" >= "minimum_price_per_square_meter"),
    CONSTRAINT "FK_structured_extraction_item_glass_valuations_glass_price_ran~" FOREIGN KEY (glass_price_range_version_id) REFERENCES core.glass_price_range_versions (id) ON DELETE RESTRICT,
    CONSTRAINT "FK_structured_extraction_item_glass_valuations_glass_types_gla~" FOREIGN KEY (glass_type_id) REFERENCES core.glass_types (id) ON DELETE RESTRICT,
    CONSTRAINT "FK_structured_extraction_item_glass_valuations_structured_extr~" FOREIGN KEY (structured_extraction_item_id) REFERENCES core.structured_extraction_items (id) ON DELETE CASCADE
);

CREATE INDEX "IX_structured_extraction_item_glass_valuations_glass_price_ran~" ON core.structured_extraction_item_glass_valuations (glass_price_range_version_id);

CREATE INDEX "IX_structured_extraction_item_glass_valuations_glass_type_id" ON core.structured_extraction_item_glass_valuations (glass_type_id);

CREATE UNIQUE INDEX "IX_structured_extraction_item_glass_valuations_structured_extr~" ON core.structured_extraction_item_glass_valuations (structured_extraction_item_id);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260731211702_AddStructuredItemGlassValuations', '10.0.10');

COMMIT;

START TRANSACTION;
ALTER TABLE core.pre_quote_draft_items ADD "ValuationStatus" integer NOT NULL DEFAULT 0;

CREATE TABLE core.pre_quote_draft_item_glass_snapshots (
    id uuid NOT NULL,
    pre_quote_draft_item_id uuid NOT NULL,
    source_structured_item_glass_id uuid NOT NULL,
    glass_type_id uuid,
    raw_specification character varying(500),
    normalized_code_snapshot character varying(30),
    assignment_scope character varying(20) NOT NULL,
    requires_review boolean NOT NULL,
    CONSTRAINT "PK_pre_quote_draft_item_glass_snapshots" PRIMARY KEY (id),
    CONSTRAINT ck_pre_quote_draft_item_glass_snapshot_identity CHECK (("normalized_code_snapshot" IS NULL AND "glass_type_id" IS NULL) OR ("normalized_code_snapshot" IS NOT NULL AND "glass_type_id" IS NOT NULL)),
    CONSTRAINT ck_pre_quote_draft_item_glass_snapshot_requirements CHECK ("requires_review" IS NOT NULL),
    CONSTRAINT ck_pre_quote_draft_item_glass_snapshot_scope CHECK ("assignment_scope" IN ('Item', 'Section', 'General', 'Unassigned')),
    CONSTRAINT "FK_pre_quote_draft_item_glass_snapshots_pre_quote_draft_items_~" FOREIGN KEY (pre_quote_draft_item_id) REFERENCES core.pre_quote_draft_items (id) ON DELETE CASCADE
);

CREATE TABLE core.pre_quote_draft_item_valuation_snapshots (
    id uuid NOT NULL,
    pre_quote_draft_item_id uuid NOT NULL,
    source_structured_item_valuation_id uuid NOT NULL,
    status character varying(20) NOT NULL,
    reason character varying(40),
    glass_type_id uuid,
    glass_price_range_version_id uuid,
    width_millimeters_used integer,
    height_millimeters_used integer,
    quantity_used integer,
    unit_area_square_meters numeric(18,6),
    total_area_square_meters numeric(18,6),
    unit_price_per_square_meter numeric(18,6),
    unit_amount numeric(18,6),
    total_amount numeric(18,6),
    currency character varying(3),
    valued_at_utc timestamp with time zone NOT NULL,
    invalidated_at_utc timestamp with time zone,
    invalidation_reason character varying(30),
    CONSTRAINT "PK_pre_quote_draft_item_valuation_snapshots" PRIMARY KEY (id),
    CONSTRAINT ck_pre_quote_draft_item_valuation_snapshot_amounts CHECK ("unit_amount" IS NULL OR "unit_amount" >= 0 AND "total_amount" >= "unit_amount"),
    CONSTRAINT ck_pre_quote_draft_item_valuation_snapshot_areas CHECK ("unit_area_square_meters" IS NULL OR "unit_area_square_meters" >= 0 AND "total_area_square_meters" >= 0),
    CONSTRAINT ck_pre_quote_draft_item_valuation_snapshot_currency CHECK ("currency" IS NULL OR char_length("currency") = 3),
    CONSTRAINT ck_pre_quote_draft_item_valuation_snapshot_prices CHECK ("unit_price_per_square_meter" IS NULL OR "unit_price_per_square_meter" > 0),
    CONSTRAINT ck_pre_quote_draft_item_valuation_snapshot_status CHECK ("status" IN ('NotApplicable', 'Pending', 'Valued', 'Stale', 'RequiresReview')),
    CONSTRAINT "FK_pre_quote_draft_item_valuation_snapshots_glass_price_range_~" FOREIGN KEY (glass_price_range_version_id) REFERENCES core.glass_price_range_versions (id) ON DELETE RESTRICT,
    CONSTRAINT "FK_pre_quote_draft_item_valuation_snapshots_glass_types_glass_~" FOREIGN KEY (glass_type_id) REFERENCES core.glass_types (id) ON DELETE RESTRICT,
    CONSTRAINT "FK_pre_quote_draft_item_valuation_snapshots_pre_quote_draft_it~" FOREIGN KEY (pre_quote_draft_item_id) REFERENCES core.pre_quote_draft_items (id) ON DELETE CASCADE
);

CREATE TABLE core.pre_quote_draft_item_glass_evidence (
    id uuid NOT NULL,
    glass_snapshot_id uuid NOT NULL,
    sequence integer NOT NULL,
    page_number integer NOT NULL,
    source_type character varying(10) NOT NULL,
    text character varying(500) NOT NULL,
    CONSTRAINT "PK_pre_quote_draft_item_glass_evidence" PRIMARY KEY (id),
    CONSTRAINT ck_pre_quote_draft_item_glass_evidence_page_number CHECK ("page_number" > 0),
    CONSTRAINT ck_pre_quote_draft_item_glass_evidence_sequence CHECK ("sequence" > 0),
    CONSTRAINT "FK_pre_quote_draft_item_glass_evidence_pre_quote_draft_item_gl~" FOREIGN KEY (glass_snapshot_id) REFERENCES core.pre_quote_draft_item_glass_snapshots (id) ON DELETE CASCADE
);

CREATE TABLE core.pre_quote_draft_item_glass_review_reasons (
    id uuid NOT NULL,
    glass_snapshot_id uuid NOT NULL,
    sequence integer NOT NULL,
    code character varying(40) NOT NULL,
    CONSTRAINT "PK_pre_quote_draft_item_glass_review_reasons" PRIMARY KEY (id),
    CONSTRAINT ck_pre_quote_draft_item_glass_review_reason_sequence CHECK ("sequence" > 0),
    CONSTRAINT "FK_pre_quote_draft_item_glass_review_reasons_pre_quote_draft_i~" FOREIGN KEY (glass_snapshot_id) REFERENCES core.pre_quote_draft_item_glass_snapshots (id) ON DELETE CASCADE
);

CREATE TABLE core.pre_quote_draft_item_glass_source_pages (
    id uuid NOT NULL,
    glass_snapshot_id uuid NOT NULL,
    sequence integer NOT NULL,
    page_number integer NOT NULL,
    CONSTRAINT "PK_pre_quote_draft_item_glass_source_pages" PRIMARY KEY (id),
    CONSTRAINT ck_pre_quote_draft_item_glass_source_page_page_number CHECK ("page_number" > 0),
    CONSTRAINT ck_pre_quote_draft_item_glass_source_page_sequence CHECK ("sequence" > 0),
    CONSTRAINT "FK_pre_quote_draft_item_glass_source_pages_pre_quote_draft_ite~" FOREIGN KEY (glass_snapshot_id) REFERENCES core.pre_quote_draft_item_glass_snapshots (id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX "IX_pre_quote_draft_item_glass_evidence_glass_snapshot_id_page_~" ON core.pre_quote_draft_item_glass_evidence (glass_snapshot_id, page_number, source_type, text);

CREATE UNIQUE INDEX "IX_pre_quote_draft_item_glass_evidence_glass_snapshot_id_seque~" ON core.pre_quote_draft_item_glass_evidence (glass_snapshot_id, sequence);

CREATE UNIQUE INDEX "IX_pre_quote_draft_item_glass_review_reasons_glass_snapshot_i~1" ON core.pre_quote_draft_item_glass_review_reasons (glass_snapshot_id, sequence);

CREATE UNIQUE INDEX "IX_pre_quote_draft_item_glass_review_reasons_glass_snapshot_id~" ON core.pre_quote_draft_item_glass_review_reasons (glass_snapshot_id, code);

CREATE UNIQUE INDEX "IX_pre_quote_draft_item_glass_source_pages_glass_snapshot_id_p~" ON core.pre_quote_draft_item_glass_source_pages (glass_snapshot_id, page_number);

CREATE UNIQUE INDEX "IX_pre_quote_draft_item_glass_source_pages_glass_snapshot_id_s~" ON core.pre_quote_draft_item_glass_source_pages (glass_snapshot_id, sequence);

CREATE UNIQUE INDEX "IX_pre_quote_draft_item_glass_snapshots_pre_quote_draft_item_id" ON core.pre_quote_draft_item_glass_snapshots (pre_quote_draft_item_id);

CREATE INDEX "IX_pre_quote_draft_item_valuation_snapshots_glass_price_range_~" ON core.pre_quote_draft_item_valuation_snapshots (glass_price_range_version_id);

CREATE INDEX "IX_pre_quote_draft_item_valuation_snapshots_glass_type_id" ON core.pre_quote_draft_item_valuation_snapshots (glass_type_id);

CREATE UNIQUE INDEX "IX_pre_quote_draft_item_valuation_snapshots_pre_quote_draft_it~" ON core.pre_quote_draft_item_valuation_snapshots (pre_quote_draft_item_id);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260804161850_AddPreQuoteDraftV3EconomicSnapshot', '10.0.10');

COMMIT;

