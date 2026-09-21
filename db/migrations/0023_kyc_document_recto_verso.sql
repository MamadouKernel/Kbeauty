ALTER TABLE etablissement
    ADD COLUMN type_document_identite VARCHAR(20),
    ADD COLUMN url_document_recto TEXT,
    ADD COLUMN url_document_verso TEXT;

ALTER TABLE etablissement
    ADD CONSTRAINT ck_etablissement_type_document_identite
    CHECK (type_document_identite IS NULL OR type_document_identite IN ('CNI', 'PASSEPORT'));

-- Compatibilite : les anciens fichiers restent consultables comme recto, mais une CNI
-- historique sans verso devra etre completee avant une nouvelle validation KYC.
UPDATE etablissement
SET type_document_identite = 'CNI',
    url_document_recto = url_piece_identite
WHERE url_piece_identite IS NOT NULL;