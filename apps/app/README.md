# Future interactive profiles

The accepted design places the Account/Chat application here, with separate build profiles and origins. The operator and status surfaces retain their own route and access boundaries. They are not implemented by this Hello bootstrap and are not linked as available products.

Reuse `packages/ui`, the root npm toolchain and released Contracts packages when implementing those profiles. Browser sessions and business APIs belong to the C# Cloud host. Do not introduce a Node business server, share parent-domain authentication cookies, or use public-site routing as an authorization boundary.
