// SPDX-License-Identifier: AGPL-3.0-only
import { useLoaderData } from "react-router";
import { metadataForPage } from "../site-metadata";
import { loadLocalizedPage } from "../site-content.server";
import { CloudHelloPageView, HelloPageView, HomePageView } from "../site-pages";

export function loader({ params }: { params: Record<string, string | undefined> }) {
  return loadLocalizedPage(params);
}

export function meta({
  loaderData,
}: {
  loaderData: Awaited<ReturnType<typeof loader>> | undefined;
}) {
  return loaderData ? metadataForPage(loaderData) : [];
}

export default function LocalizedSitePage() {
  const page = useLoaderData<typeof loader>();
  switch (page.kind) {
    case "home":
      return <HomePageView page={page} />;
    case "hello":
      return <HelloPageView page={page} />;
    case "cloud-hello":
      return <CloudHelloPageView page={page} />;
    case "document":
      return (
        <article className="example">
          <a className="back-link" href="/">
            ← Back home
          </a>
          <p className="eyebrow">Documentation · {page.version}</p>
          <h1>{page.title}</h1>
          <p className="hero-copy">{page.description}</p>
          <p>{page.body}</p>
        </article>
      );
  }
}
