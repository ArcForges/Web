// SPDX-License-Identifier: AGPL-3.0-only
import { useLoaderData } from "react-router";
import { loadCloudHelloPage } from "../site-content.server";
import { metadataForPage } from "../site-metadata";
import { CloudHelloPageView } from "../site-pages";

export function loader() {
  return loadCloudHelloPage();
}

export function meta({
  loaderData,
}: {
  loaderData: ReturnType<typeof loadCloudHelloPage> | undefined;
}) {
  return loaderData ? metadataForPage(loaderData) : [];
}

export default function CloudHello() {
  const page = useLoaderData<typeof loader>();
  return <CloudHelloPageView page={page} />;
}
