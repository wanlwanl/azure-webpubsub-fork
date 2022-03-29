import { createApp } from 'vue'
import App from '@/App.vue'
import store from '@/store'
import ElementPlus from 'element-plus'
import 'element-plus/dist/index.css'
import VueGtag from 'vue-gtag'

createApp(App)
    .use(VueGtag, {
        config: { id: 'G-5LSB8YMW7Y' },
    })
    .use(store)
    .use(ElementPlus)
    .mount('#app')
