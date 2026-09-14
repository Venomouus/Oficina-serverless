# Certificados publicos RDS

`rds-global-bundle.pem` foi obtido de https://truststore.pki.rds.amazonaws.com/global/global-bundle.pem em 2026-09-14.

SHA-256: `E5BB2084CCF45087BDA1C9BFFDEA0EB15EE67F0B91646106E466714F9DE3C7E3`.

Contem somente certificados publicos de CA; nao e a chave JWT. O projeto inclui o arquivo no publish e a Lambda usa `SSL Mode=VerifyFull`, validando cadeia e hostname.

Atualizar o bundle a partir da mesma fonte oficial antes da expiracao/rotacao de CA ou expansao para novas regioes; revisar o diff e registrar o novo hash. Nao baixar certificados durante cada invocacao. [Documentacao AWS](https://docs.aws.amazon.com/AmazonRDS/latest/UserGuide/UsingWithRDS.SSL.html).
