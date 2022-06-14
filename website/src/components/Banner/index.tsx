import React from 'react'
import { Swiper, SwiperSlide } from 'swiper/react'
import { Pagination, Navigation } from 'swiper'
import 'swiper/css'
import 'swiper/css/pagination'
import 'swiper/css/navigation'

import styles from './styles.module.css'
import { IsWideDevice } from '@site/src/utils/CssUtils'

export default function Banner() {
  const isWide = IsWideDevice()
  const imageSource = (index: number) => `/img/banner-${isWide ? 'desktop' : 'mobile'}-${index}.jpg`

  const slides = [1, 2, 3, 4].map(ind => (
    <SwiperSlide>
      <img src={imageSource(ind)} className={styles.bannerImage}></img>
    </SwiperSlide>
  ))

  return (
    <Swiper
      slidesPerView={1}
      spaceBetween={30}
      loop={true}
      pagination={{
        clickable: true,
      }}
      navigation={isWide}
      modules={[Pagination, Navigation]}
    >
      {slides}
    </Swiper>
  )
}
