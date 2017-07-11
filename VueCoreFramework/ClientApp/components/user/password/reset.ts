﻿import Vue from 'vue';
import { Component, Prop } from 'vue-property-decorator';
import { checkResponse } from '../../../router';
import VueFormGenerator from 'vue-form-generator';
import { Schema, VFGOptions } from '../../../vfg/vfg';
import * as VFG_Custom from '../../../vfg/vfg-custom-validators';
import * as ErrorMsg from '../../../error-msg';

/**
 * A ViewModel used to transfer information during a user account password reset.
 */
interface ResetPasswordViewModel {
    /**
     * The email address of the user account.
     */
    email: string;

    /**
     * The new password for the user account.
     */
    newPassword: string;

    /**
     * The new password for the user account, repeated.
     */
    confirmPassword: string;

    /**
     * The code generated to identify the callback link in the email generated by the operation.
     */
    code: string;
}

@Component
export default class ResetComponent extends Vue {
    @Prop()
    code: string;

    components = {
        'vue-form-generator': VueFormGenerator.component
    };

    errors: string[] = [];

    model: ResetPasswordViewModel = {
        email: '',
        newPassword: '',
        confirmPassword: '',
        code: this.code
    };

    schema: Schema = {
        fields: [
            {
                type: 'input',
                inputType: 'email',
                model: 'email',
                placeholder: 'Email',
                autocomplete: true,
                required: true,
                validator: VFG_Custom.requireEmail
            },
            {
                type: 'input',
                inputType: 'password',
                model: 'newPassword',
                placeholder: 'New Password',
                min: 6,
                max: 24,
                required: true,
                validator: VFG_Custom.requireNewPassword
            },
            {
                type: 'input',
                inputType: 'password',
                model: 'confirmPassword',
                placeholder: 'Confirm Password',
                required: true,
                validator: VFG_Custom.requirePasswordMatch
            }
        ]
    };

    formOptions: VFGOptions = {
        validateAfterChanged: true
    };

    isValid = false;
    onValidated(isValid: boolean, errors: Array<any>) {
        this.isValid = isValid;
    }

    changeSuccess = false;
    submitting = false;

    onSubmit() {
        if (!this.isValid) return;
        this.submitting = true;
        fetch('api/Account/ResetPassword',
            {
                method: 'POST',
                headers: {
                    'Accept': `application/json;v=${this.$store.state.apiVer}`,
                    'Accept-Language': this.$store.state.userState.culture,
                    'Content-Type': `application/json;v=${this.$store.state.apiVer}`
                },
                body: JSON.stringify(this.model)
            })
            .then(response => checkResponse(response, this.$route.fullPath))
            .then(response => {
                if (!response.ok) {
                    if (response.statusText) {
                        this.errors = response.statusText.split(';');
                    } else {
                        this.errors.push("A problem occurred.");
                    }
                    throw new Error(response.statusText);
                }
                return response;
            })
            .then(response => {
                this.changeSuccess = true;
                this.submitting = false;
            })
            .catch(error => {
                if (this.errors.length === 0) {
                    this.errors.push("A problem occurred. Your request was not received.");
                    ErrorMsg.logError("reset.onSubmit", new Error(error));
                }
                this.submitting = false;
            });
    }
}
