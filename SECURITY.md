# Security

`.comp` packages are treated as untrusted input.

Please report security-sensitive parsing, path traversal, memory exhaustion, image-decoder, or package-validation issues privately to the repository owner rather than publishing exploit details first.

The project will not intentionally load assets from paths outside the selected `.comp` package. Any code change that relaxes this boundary requires explicit security review.
