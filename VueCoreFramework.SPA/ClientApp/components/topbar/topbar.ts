﻿import Vue from 'vue';
import VueRouter, { Route } from 'vue-router';
import { Component, Prop, Watch } from 'vue-property-decorator';
import * as Store from '../../store/store';
import { authenticate, authMgr, logout } from '../../authorization';
import * as moment from 'moment';

@Component
export default class TopbarComponent extends Vue {
    signedIn = false;
    lastUpdate: number = 0;
    updateTimeout = 0;

    get totalUnread() {
        return this.$store.state.uiState.messaging.conversations
            .map(c => c.unreadCount)
            .reduce((a, b) => { return a + b; }, 0)
            + this.$store.state.uiState.messaging.systemMessages
                .filter(m => !m.received).length;
    }

    mounted() {
        if (this.updateTimeout === 0) {
            this.updateTimeout = setTimeout(this.updateAuth, 500);
        }
    }

    @Watch('$route')
    onRouteChange(val: Route, oldVal: Route) {
        if (this.updateTimeout === 0) {
            this.updateTimeout = setTimeout(this.updateAuth, 500);
        }
    }

    getReturnUrl() {
        let returnUrl: string = this.$route.query.returnUrl;
        if (!returnUrl) {
            returnUrl = this.$route.fullPath;
        }
        return returnUrl;
    }

    onLogin() {
        this.$router.push({ path: '/login', query: { returnUrl: this.getReturnUrl() } });
    }

    async onLogout() {
        this.signedIn = false;
        await logout();
        this.$router.push('/');
    }

    onToggleChat() {
        if (!this.$store.state.uiState.messaging.messagingShown) {
            this.$store.dispatch(Store.refreshGroups, this.$route.fullPath);
            this.$store.dispatch(Store.refreshConversations, this.$route.fullPath);
            this.$store.dispatch(Store.refreshChat, this.$route.fullPath);
        }
        this.$store.commit(Store.toggleMessaging);
    }

    async updateAuth() {
        this.updateTimeout = 0;
        let auth = await authenticate();
        this.signedIn = auth === "authorized";
    }
}