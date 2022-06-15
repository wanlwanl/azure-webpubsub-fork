import React from 'react'
import useDocusaurusContext from '@docusaurus/useDocusaurusContext'

import Demos from '@site/src/components/Demos'
import CustomerStories from '../components/CustomerStories'
import Feedback from '../components/Feedback'
import Banner from '../components/Banner'
import Layout from '@theme/Layout'
import Introduction from '../components/Introduction'

export default function Home(): JSX.Element {
  const { siteConfig } = useDocusaurusContext()

  return (
    <Layout title={siteConfig.title}>
      <Banner></Banner>
      <Introduction hidden={true}></Introduction>
      <Demos hidden={false}></Demos>
      <CustomerStories></CustomerStories>
      <Feedback></Feedback>
    </Layout>
  )
}
