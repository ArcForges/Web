// SPDX-License-Identifier: AGPL-3.0-only
import { useLoaderData } from "react-router";
import { loadHelloPage } from "../site-content.server";
import { metadataForPage } from "../site-metadata";
import { HelloPageView } from "../site-pages";

export function loader() {
  return loadHelloPage();
}

export function meta({ loaderData }: { loaderData: ReturnType<typeof loadHelloPage> | undefined }) {
  return loaderData ? metadataForPage(loaderData) : [];
}

export default function Hello() {
  const page = useLoaderData<typeof loader>();
  return <HelloPageView page={page} />;
}
